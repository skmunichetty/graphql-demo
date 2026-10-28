# Practical: deploy the Estimates API to AWS ECS Fargate

A hands-on learning guide in small stages. Scope of this first practical: **only
`src/Estimates/Estimates.Api`**, one `dev` environment in **ap-southeast-2 (Sydney)**. The
Accounting service follows in a later practical.

| Stage | What you do | Creates AWS resources? |
|---|---|---|
| 1 | Inspect the repo, prove the container runs locally, make it load-balancer ready | No |
| 2 | Terraform bootstrap: state bucket, GitHub OIDC role, ECR | Yes (cents per month) |
| 3 | Terraform dev: VPC, RDS, ALB, ECS | **Yes (about US$2 per day)** |
| 4 | GitHub Actions and the first deployment | Uses stage 2/3 resources |
| 5 | A small API change rolls out as a new task definition revision | — |
| 6 | Rollback to an earlier image digest, then clean up | Removes resources |

---

## ECS in Kubernetes terms

| ECS | Plain meaning | Closest Kubernetes idea |
|---|---|---|
| **Cluster** | A named grouping of services and tasks. With Fargate there are no servers to manage in it | Cluster/namespace, but with no nodes for you to run |
| **Task definition** | A versioned, immutable recipe: image, CPU/memory, ports, env vars, secrets, health check, roles. Each change creates a new *revision* (`estimates-api:7`) | A Pod template (the `spec.template` of a Deployment) |
| **Task** | One running copy of a task definition | A Pod |
| **Service** | Keeps N tasks running, replaces failed ones, rolls out new revisions, and registers tasks with a load balancer target group | Deployment + Service (+ part of what an Ingress controller does) |
| **Fargate** | AWS runs the task on capacity you never see. You pay per vCPU-second and GB-second | Like a cluster with virtual nodes (EKS Fargate profile, virtual-kubelet) |
| **Execution role** | Used by the **ECS agent, before your code starts**: pull the image from ECR, fetch secrets from Secrets Manager, create the log stream | Roughly the kubelet's permissions (image pull secrets + the secrets store CSI driver) |
| **Task role** | Used by **your application code** when it calls AWS APIs | IRSA / EKS Pod Identity (the Pod's service account role) |
| **One-off task** (`aws ecs run-task`) | A task started without a service: it runs, exits, and you read its exit code | A Kubernetes Job |

Two consequences that surprise Kubernetes users:

- **There is no `kubectl apply` of a whole manifest.** A deployment means "register a new task
  definition revision, then point the service at it". In this repository **Terraform owns both**,
  so a deployment is a `terraform apply` with a new image digest.
- **`awsvpc` networking gives every task its own network interface and private IP** in your
  subnets, with its own security group. That's why the load balancer target group uses
  *IP* targets, not instance targets.

---

## Design for this practical

```
Internet ──443/80──▶ ALB (public subnets, 2 AZs, SG: alb)
                       │ HTTP :8080 (only from the ALB security group)
                       ▼
              ECS service "estimates-api" (Fargate, 1 task, 0.25 vCPU / 0.5 GB)
              task in a public subnet with a public IP, SG: api
                       │ 5432 (only from the api security group)
                       ▼
              RDS PostgreSQL 17, db.t4g.micro, single AZ, NOT publicly accessible
              (private database subnets, no internet route)

Task's outbound traffic over its public IP: ECR (image), CloudWatch Logs, Secrets Manager, OIDC issuer (signing keys)
```

**The learning trade-off in this network.** Production designs put tasks in *private* subnets and
reach AWS through a NAT gateway (about US$45/month per AZ in Sydney, plus data charges) or VPC
endpoints (about US$10/month each, per AZ). Here, tasks sit in public subnets with a public IP
so they can reach ECR, CloudWatch, Secrets Manager and the identity provider directly. **Nothing
on the internet can connect *to* a task.** Its security group only accepts port 8080 from the
load balancer's security group. The database has no internet route at all. What you give up: a
task's outbound traffic leaves directly from its public IP, so there is no single egress point to
filter or log. Each public IPv4 address costs US$0.005/hour.

Deliberately **not** included yet: NAT gateways, autoscaling, multiple environments, reusable module
hierarchies, Kubernetes.

### First-deployment order (the chicken-and-egg problem)

```
1. bootstrap (local AWS login)  → state bucket, GitHub OIDC deploy role, ECR repository
2. GitHub Actions builds image  → pushes <sha> to ECR, captures its digest (sha256:…)
3. terraform apply (phase 1)    → VPC, RDS, ALB, ECS cluster, migration task definition,
                                  API service with desired count 0 (nothing can start yet)
4. aws ecs run-task (migrate)   → one-off task runs --migrate-database; pipeline checks exit code 0
5. terraform apply (phase 2)    → API task definition with the new digest, desired count 1
6. wait for ECS stability, then check https://…/health/ready through the ALB
```

Every later deployment follows the same two-phase path, so the API never runs against a schema
older than it expects. No `terraform -target` is needed. The details are in stage 4.

---

## Costs (verified 2026-10-25 with the AWS Price List API, Sydney, on-demand, USD)

| Item | Price | Running 24/7 for a month (730 h) |
|---|---|---|
| Application Load Balancer | $0.0252/h + $0.008 per LCU-hour | ≈ $18.40 + ~$1–6 LCU at low traffic |
| Fargate task, 0.25 vCPU + 0.5 GB | $0.04856 per vCPU-hour, $0.00532 per GB-hour → $0.0148/h | ≈ $10.80 (migration tasks: seconds, negligible) |
| Public IPv4 (ALB ×2 AZs + task ×1) | $0.005/h each | ≈ $10.95 (+1 address briefly during each rollout) |
| RDS PostgreSQL db.t4g.micro, single AZ | $0.025/h | ≈ $18.25 |
| RDS gp3 storage, 20 GB | $0.138/GB-month | ≈ $2.76 (automated backups up to the DB size are free) |
| Secrets Manager | $0.40/secret-month | ≈ $0.40 |
| CloudWatch Logs | $0.67/GB ingested, $0.033/GB-month stored, 7-day retention | < $1 at this volume |
| ECR | $0.10/GB-month | < $0.50 (lifecycle policy keeps the last 20 images) |
| S3 state bucket, ACM public certificate, IAM | — | ≈ $0 (ACM public certificates are free) |
| **Total** | | **≈ US$62–68/month, about $2/day** |

Data transfer out of AWS has a 100 GB/month free allowance, which this practical won't exceed.
The biggest savings come from **destroying the dev stack when you're not using it** (stage 6).
The bootstrap stack costs cents and can stay.

---

## Stage 1 — inspect the repository and prove the container

### What the inspection found

| Item | Finding | Consequence for ECS |
|---|---|---|
| .NET | `global.json` SDK 10.0.1xx; `net10.0` everywhere (`Directory.Build.props`); warnings are errors | Use the `sdk:10.0` / `aspnet:10.0` images (already in the Dockerfile) |
| Dockerfile | Multi-stage, build context = repo root, copies only Estimates and ServiceDefaults, runs as non-root `$APP_UID`, `ASPNETCORE_HTTP_PORTS=8080` | Container port **8080**; image is `linux/amd64` when built on GitHub's Ubuntu runners |
| Configuration | `Authentication:Authority` / `Audience` / `TenantClaimType`, `ConnectionStrings:EstimatesDb`, `Hosting:EnforceHttps` (default **true**), `Hosting:ApplyMigrationsOnStartup` (default **false**). Validated at startup | Supplied as ECS environment variables (`Authentication__Authority`…) and a Secrets Manager secret |
| Authentication | JWT bearer: issuer, audience, lifetime, RS256 signature. In Production the authority **must be HTTPS** | The local Keycloak (`http://localhost:8180`) cannot be used from AWS. **A public HTTPS OIDC issuer is required** |
| Health | `/health/live` (process only), `/health/ready` (database) | live → container health check; ready → ALB target health check |
| Migrations | Only `ApplyMigrationsOnStartup`, which runs in every replica | Needed a one-off migration mode (added below) |
| CI | `.github/workflows/ci.yml`: restore, build, `dotnet test --solution`, docker build of both images, on push to main and on PRs | Extended in stage 4 |
| Repo instructions | No CLAUDE.md, AGENTS.md or Copilot instructions | — |
| Other branches | `infra/m2-aws-foundation` (unmerged EKS design), `feature/dev-token-endpoint`, `feature/estimate-subscriptions` (unmerged) | This practical branches from `main` and doesn't depend on them |

### What went wrong behind a load balancer (reproduced locally first)

The unchanged image was run in Production mode, with requests sent the way an ALB sends them:

| Situation | Result | Why it matters |
|---|---|---|
| Authority is `http://…` in Production | **Every request returns 500**, including `/health/live` | JwtBearer rejects non-HTTPS metadata, and authentication runs on every request. Correct behaviour, but it means ECS needs a real HTTPS issuer |
| `EnforceHttps=true`, HTTPS port unknown | Plain-HTTP GraphQL is **served**, not redirected (`Failed to determine the https port for redirect`) | A silent security gap: tokens could travel over HTTP |
| HTTPS port known (`ASPNETCORE_HTTPS_PORT=443`) | Plain-HTTP `/health/*` → **307** | ALB health checks would fail and ECS would replace the task forever |
| `--migrate` mode | Didn't exist | Migrations would run inside every API replica |
| `curl`/`wget` in the image | Neither installed | No way to run a container health check |
| Database unreachable | `/health/ready` took **~20 s** to answer | Longer than a load balancer's health-check timeout; slow checks pile up |
| Npgsql on the slim image | Prints `Cannot load library libgssapi_krb5.so.2` (non-JSON) on each connection | Noise in CloudWatch; turned off with `GSS Encryption Mode=Disable` in the connection string |

### What changed and why

| File | Change |
|---|---|
| `src/ServiceDefaults/HttpSetup.cs` | `UseHttpsEnforcement()`: HSTS + HTTPS redirect for everything **except `/health/*`**. `AddReadinessCheckTimeout()`: readiness checks are capped (3 s) |
| `src/ServiceDefaults/HealthProbe.cs` | `--health-check` mode: the container calls its own `/health/live` on localhost and exits 0 or 1 (no curl needed) |
| `src/Estimates/Estimates.Infrastructure/Persistence/DatabaseMigrator.cs` | `--migrate-database` mode: applies pending EF Core migrations, logs what it did, returns exit code 0 or 1. Already-applied migrations are skipped |
| `src/Estimates/Estimates.Api/Program.cs` | Handles both modes. `ApplyMigrationsOnStartup` (local only) reuses the migrator. Uses `UseHttpsEnforcement()` and the readiness timeout |
| `tests/Estimates.IntegrationTests/BehindLoadBalancerTests.cs` | Plain-HTTP health → 200; plain-HTTP GraphQL → 307 to https; `X-Forwarded-Proto: https` → no loop (401 without a token); migration mode → exit 0 |

Local development is unchanged. `docker compose`, `dotnet run` and the existing tests behave as
before, because Development keeps `EnforceHttps=false` and `ApplyMigrationsOnStartup=true`.

How these settings map onto ECS (applied in stage 3):

```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true   # trust X-Forwarded-Proto/For (only the ALB can reach the task)
ASPNETCORE_HTTPS_PORT=443                  # makes the HTTPS redirect actually redirect
Authentication__Authority=https://…        # public HTTPS OIDC issuer (must equal the token "iss")
Authentication__Audience=estimates-api
ConnectionStrings__EstimatesDb=<from Secrets Manager>   # …;SSL Mode=Require;GSS Encryption Mode=Disable
Container health check: ["CMD", "dotnet", "/app/Estimates.Api.dll", "--health-check"]
Migration task command override: ["--migrate-database"]
```

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` tells ASP.NET Core to trust forwarded headers from any
sender. That is safe here only because the task's security group accepts traffic from the ALB
alone, so no client can reach the task directly to forge `X-Forwarded-Proto`.

### Commands to reproduce Stage 1

Start local infrastructure and build the image:

```powershell
./scripts/init-dev-env.ps1          # once; creates .env and user secrets
docker compose up -d postgres keycloak
docker build -f src/Estimates/Estimates.Api/Dockerfile -t estimates-api:local .
```

Run all tests (expected: `total: 106, failed: 0`):

```bash
dotnet test --solution ConstructionEstimation.slnx
```

Run migration mode as a one-off container. Expected: exit code `0`, and a log line
`Applying 1 migration(s)` the first time, `up to date` afterwards. Put the connection string in a
temporary env file, so the password doesn't appear in `docker inspect` or shell history:

```powershell
# stage1.env (delete it afterwards):
#   ASPNETCORE_ENVIRONMENT=Production
#   ConnectionStrings__EstimatesDb=Host=postgres;Database=estimates;Username=estimates_app;Password=<from .env>;GSS Encryption Mode=Disable
docker run --rm --network construction-estimation_default --env-file stage1.env estimates-api:local --migrate-database
echo $LASTEXITCODE
```

Run the API the way ECS will, using Docker's health check as the stand-in for the ECS container
health check. Expected: `docker inspect -f '{{.State.Health.Status}}' ecs-like-api` → `healthy`.
Add `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, `ASPNETCORE_HTTPS_PORT=443` and
`Authentication__Authority=https://identity.example.invalid/` to the env file first:

```powershell
docker run -d --name ecs-like-api --network construction-estimation_default -p 5204:8080 --env-file stage1.env `
  --health-cmd "dotnet /app/Estimates.Api.dll --health-check" --health-interval 5s estimates-api:local
```

Expected responses:

| Request | Expected |
|---|---|
| `GET http://localhost:5204/health/live` | 200 |
| `GET http://localhost:5204/health/ready` | 200 (503 within ~4 s if the database is unreachable) |
| `POST http://localhost:5204/graphql` | 307 → `https://localhost/graphql` |
| `POST …/graphql` with header `X-Forwarded-Proto: https` | 401 (no token, no redirect loop) |

### AWS console

Nothing to inspect yet. Stage 1 creates no AWS resources.

### Check your understanding

1. Why does the ALB target group use `/health/ready`, while the ECS container health check uses
   `/health/live`? What would go wrong during a short database outage if the container health
   check used `/health/ready`?
2. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` trusts `X-Forwarded-Proto` from any sender. Which
   piece of the design makes that safe here, and what could an attacker do without it?

---

## Stage 2 — Terraform bootstrap (`infra/bootstrap/`)

### Concepts

- **Why a separate bootstrap stack?** Some things must exist *before* the normal pipeline can run:
  - the S3 bucket that stores Terraform state;
  - the IAM role that GitHub Actions assumes;
  - the ECR repository the first image is pushed to.

  These are long-lived and rarely change. The dev stack can be destroyed and recreated every day
  without touching them.
- **Terraform state** is Terraform's record of what it created. It lives in S3 so that your laptop
  and GitHub Actions share one copy. `use_lockfile = true` uses S3's native conditional writes as a
  lock (no DynamoDB table), so two applies can never run at once. Bootstrap and dev use **separate
  keys** (`bootstrap/terraform.tfstate`, `dev/terraform.tfstate`), so a mistake in one can't corrupt the other.
- **GitHub OIDC instead of access keys.** Each workflow job can ask GitHub for a short-lived signed
  token that says "this is repo `skmunichetty/graphql-demo`, environment `dev`". AWS STS checks the
  signature, then checks the role's *trust policy*. Only that exact subject
  (`repo:skmunichetty/graphql-demo:environment:dev`) may assume the roles. No AWS keys exist in GitHub.
  In Kubernetes terms, it's the same idea as IRSA, with GitHub as the identity provider.
- **Why pull requests can't deploy.** The `dev` environment only accepts the `main` branch. A pull
  request (even one that edits a workflow to say `environment: dev`) is refused before its job starts,
  so it can never get a token with the `environment:dev` subject. CI jobs also don't request
  `id-token` permission at all.
- **Permissions boundary.** The deploy role must be able to create IAM roles (the ECS execution and
  task roles). Without a limit, it could create an administrator role and escalate. Every role it
  creates must carry `graphql-demo-dev-workload-boundary`, which caps permissions at: pull images,
  write logs, read this project's secrets. The deploy role can't remove that boundary, edit the
  boundary policy, or modify its own role.

### What bootstrap creates (18 resources)

| Resource | Purpose |
|---|---|
| S3 `graphql-demo-tfstate-<account>` | State for both stacks: versioned, SSE-S3 encrypted, public access blocked, TLS-only policy, old versions expire after 90 days, `prevent_destroy` |
| ECR `graphql-demo/estimates-api` | **Immutable tags**, scan on push, untagged images removed after 1 day, last 20 images kept |
| IAM role `graphql-demo-dev-github-deploy` | Terraform for `infra/dev`, image push, migration task |
| IAM role `graphql-demo-dev-github-ops` | Narrow: stop/start this service and database, read costs, send the report |
| IAM policy `graphql-demo-dev-workload-boundary` | The permissions boundary above |
| SNS topic `graphql-demo-dev-alerts` + email subscription | Daily usage report |
| Budget `graphql-demo-monthly` (US$75) | Email at 50/80/100 % of actual spend and 100 % of the forecast |
| Cost allocation tag `Project` (activated) | Lets Billing split costs by project; applies from activation onward (up to 24 h) |

The account's existing GitHub OIDC provider is **read, not owned**: other projects use it.

### The first run: local backend first, then move state to S3

The stack creates the bucket its state will live in, so the first apply can't use that bucket.
A temporary override file solves it. `*_override.tf` is gitignored, and Terraform merges it over
`versions.tf`. You run this once, with your local AWS login, before GitHub can assume any role:

```powershell
cd infra/bootstrap
Copy-Item terraform.tfvars.example terraform.tfvars      # set alert_email (gitignored)
Set-Content backend_override.tf 'terraform { backend "local" {} }'
terraform init
terraform plan -out=bootstrap.tfplan     # expected: Plan: 18 to add, 0 to change, 0 to destroy
terraform apply bootstrap.tfplan

$bucket = terraform output -raw state_bucket
Remove-Item backend_override.tf, bootstrap.tfplan
terraform init -migrate-state -force-copy "-backend-config=bucket=$bucket"
terraform plan                           # expected: No changes.
Remove-Item terraform.tfstate*           # the state now lives in S3
```

Results when this was run (2026-10-26): 18 added; state migrated to
`s3://graphql-demo-tfstate-…/bootstrap/terraform.tfstate`; the follow-up plan showed **No changes**.

**Confirm the email subscription.** AWS sends "AWS Notification - Subscription Confirmation" to the
alert address. Until you click it, the topic shows `PendingConfirmation` and reports aren't delivered.

### Sensitive data and state

| Where | What could be sensitive | Protection |
|---|---|---|
| Bootstrap state | Role ARNs, bucket name, **the alert email** (variable values are stored in state) | Private, encrypted, TLS-only bucket; only you and the dev deploy role can read it (and that role only `dev/*`) |
| Dev state | Hostnames, ARNs, security group IDs. **Not** the database password (write-only, see stage 3) | Same bucket, separate key |
| `terraform.tfvars` | The alert email | Gitignored; the repository is public |
| Plan files | Same as state | Created in the runner's temporary workspace and discarded; `*.tfplan` is gitignored |

### AWS console

- **S3 → graphql-demo-tfstate-…**: *Properties* shows Versioning enabled and default encryption;
  *Permissions* shows Block all public access and the `DenyInsecureTransport` policy.
- **IAM → Roles → graphql-demo-dev-github-deploy → Trust relationships**: the `sub` condition
  `repo:skmunichetty/graphql-demo:environment:dev`.
- **ECR → graphql-demo/estimates-api**: Tag immutability *Enabled*, scan on push.
- **Billing → Budgets → graphql-demo-monthly**, and **Billing → Cost allocation tags**: `Project` *Active*.

### Check your understanding

1. A pull request edits `deploy-dev.yml` to add `environment: dev` to a job. What exactly stops
   that job from getting AWS credentials?
2. The deploy role can create IAM roles. Why can't it create one with `AdministratorAccess` and use it?

---

## Stage 3 — the dev stack (`infra/dev/`)

### Concepts

- **VPC layout** (`network.tf`): two public subnets (ALB and tasks) and two database subnets in two
  AZs. The database subnets' route table has only the implicit `local` route, so nothing in them can
  reach or be reached from the internet. RDS requires a subnet group spanning two AZs even for a
  single-AZ instance.
- **Security groups as a chain** (`security.tf`): `internet → alb (443/80) → api (8080) → db (5432)`.
  Each rule names the *previous security group*, not an IP range. Task IPs change on every
  deployment, so group references are what make the rules stable.
- **ALB, target group and health checks** (`alb.tf`): the HTTPS listener terminates TLS with a
  DNS-validated ACM certificate for `estimates-dev.amapi.click`. Port 80 is redirected to 443 by the
  ALB itself. The target group uses **IP targets**, because each Fargate task has its own network
  interface (`awsvpc`). Its health check is `/health/ready` every 15 s, with a 5 s timeout.
- **Two health checks, two jobs**: ALB → `/health/ready` decides *who gets traffic*. The ECS container
  health check → `--health-check` (liveness) decides *who gets restarted*. A database outage removes
  tasks from the ALB but doesn't make ECS kill them.
- **Database password never in state** (`database.tf`): `ephemeral "random_password"` produces a
  value that exists only during one Terraform run. It's sent to RDS through `password_wo` and to
  Secrets Manager through `secret_string_wo`, which are *write-only* arguments, never stored in
  state or plans. Only `password_wo_version = 1` is stored. Raise `db_password_version` to rotate.
  The plan shows `password_wo = (write-only attribute)`.
- **Execution role vs task role** (`ecs.tf`): the *execution* role is used by ECS **before** the app
  starts (pull from ECR, create the log stream, read the connection-string secret). The *task* role
  is the running app's identity. It has no permissions, because the API calls no AWS APIs.
- **Terraform owns the image.** Both task definitions take an image **digest** variable. The service
  is created with `desired_count = 0` so nothing can start before the first migration. After that,
  `desired_count` is ignored by Terraform, because the deploy and stop/start workflows own it.

### Local validation (no resources created)

```powershell
$bucket = terraform -chdir=infra/bootstrap output -raw state_bucket
terraform -chdir=infra/dev init "-backend-config=bucket=$bucket"
terraform -chdir=infra/dev validate
$z = 'sha256:' + ('0' * 64)
terraform -chdir=infra/dev plan -lock=false "-var=api_image_digest=$z" "-var=migration_image_digest=$z" `
  "-var=domain_name=estimates-dev.amapi.click" "-var=route53_zone_name=amapi.click" `
  "-var=oidc_authority=https://oidc-not-configured.invalid/"
```

Result when run: `Plan: 44 to add, 0 to change, 0 to destroy`, with `password_wo` and
`secret_string_wo` shown as `(write-only attribute)`. Don't apply this plan locally: the digest is a
placeholder. GitHub Actions does the real apply in stage 4.

### AWS console (after the first deployment)

- **VPC → Your VPCs → graphql-demo-dev → Resource map**: subnets, route tables, internet gateway.
- **EC2 → Load Balancers → graphql-demo-dev → Listeners**: 443 with the certificate, 80 redirect.
  **Target groups → graphql-demo-dev-api → Targets**: one *healthy* IP target.
- **RDS → Databases → graphql-demo-dev-estimates**: *Publicly accessible: No*, the db security group.
- **Secrets Manager → graphql-demo/dev/estimates-db**: you can *Retrieve secret value* as an admin;
  ECS reads only the `connectionString` key.
- **ECS → Clusters → graphql-demo-dev → Services → estimates-api**: *Deployments*, *Tasks*,
  *Events*, and *Health and metrics*. Open the task to see its private and public IP and the
  container health status.

### Check your understanding

1. Each task has a public IP. Why can't someone on the internet connect to it on port 8080?
2. Where does the database password exist after `terraform apply`, and where does it **not** exist?

---

## Stage 4 — GitHub Actions and the first deployment

### Workflows

| File | Trigger | AWS role | What it does |
|---|---|---|---|
| `ci.yml` | Pull requests; called by deploy | **None** | Restore, build, all tests, build both images, `terraform fmt -check` and `validate` (no backend, no credentials) |
| `deploy-dev.yml` | Push to `main`; manual (optional `image_digest` for rollback) | deploy | CI → build/push → two-phase Terraform with a migration task in between → rollout check → public checks |
| `dev-power.yml` | Manual stop/start; **automatic stop nightly at 10:00 UTC** | ops | Scale ECS to 0 and stop RDS, or the reverse |
| `cost-report.yml` | Daily at 21:00 UTC; manual | ops | Emails the usage report through SNS |

All jobs that touch AWS use `environment: dev`, OIDC (`id-token: write`) and
`mask-aws-account-id: true` (the repository and its Actions logs are public). Deploy and stop/start
share the concurrency group `dev-environment`, so they never overlap, and a running Terraform apply
is never cancelled.

### The deployment, step by step

```
CI (tests, image build, terraform validate)
→ build and push  <ecr>/graphql-demo/estimates-api:<commit sha>   (immutable tag)
→ deploy BY DIGEST  sha256:…   (the exact bytes that were tested)
→ read current API digest from Terraform state      (empty on the first deployment)
→ start RDS if the environment was stopped
→ PHASE 1  terraform plan + apply
     migration_image_digest = NEW,  api_image_digest = CURRENT (or NEW on the first deploy, while the service has 0 tasks)
→ aws ecs run-task (migration task), wait for STOPPED, print its log, require exit code 0
→ PHASE 2  terraform plan + apply   api_image_digest = NEW   → new task definition revision
→ desired count 1 if it was 0 → wait services-stable → PRIMARY deployment must be the new revision
  with rolloutState COMPLETED (the circuit breaker may have rolled back)
→ https://…/health/ready = 200,  http://… = 301,  POST /graphql without a token = 401
```

The first deployment follows the same steps: the service is created with 0 tasks in phase 1, so
it can't start before the migration; then phase 2 plus "desired count 1" starts it. No `-target`.

### GitHub setup (done)

- Environment **`dev`**, deployment branches: **`main` only**.
- Environment variables: `AWS_REGION`, `AWS_DEPLOY_ROLE_ARN`, `AWS_OPS_ROLE_ARN`, `TF_STATE_BUCKET`,
  `ALERTS_TOPIC_ARN`, `ECR_REPOSITORY`, `DEV_DOMAIN`, `ROUTE53_ZONE`, `ECS_CLUSTER`, `ECS_SERVICE`,
  `DB_INSTANCE_ID`, `OIDC_AUTHORITY`. No secrets are stored in GitHub.

The same setup from the command line (bash):

```bash
echo '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' \
  | gh api -X PUT repos/skmunichetty/graphql-demo/environments/dev --input -
gh api -X POST repos/skmunichetty/graphql-demo/environments/dev/deployment-branch-policies -f name=main -f type=branch
gh variable set AWS_DEPLOY_ROLE_ARN --env dev --body "$(terraform -chdir=infra/bootstrap output -raw github_deploy_role_arn)"
```

### The first deployment

Merging the pull request into `main` starts `Deploy dev`. Expect about 15–20 minutes, mostly RDS
creation (~6–10 min) and ACM validation (~1–5 min). Watch it under **Actions → Deploy dev**.
Expected end state: job summary *success*, the environment URL `https://estimates-dev.amapi.click`,
and in the log: `Migration task completed successfully (exit code 0)` and
`Rollout of graphql-demo-dev-estimates-api:N completed.`

Check it yourself:

```bash
curl -i https://estimates-dev.amapi.click/health/ready           # 200 Healthy (database reachable)
curl -i http://estimates-dev.amapi.click/health/live             # 301 to https (ALB redirect)
curl -i -X POST https://estimates-dev.amapi.click/graphql \
  -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}'   # 401: authentication enforced
```

### What is still open: the authenticated test

There's no public HTTPS identity provider yet, so `OIDC_AUTHORITY` is the placeholder
`https://oidc-not-configured.invalid/`. Authentication is **not** disabled: the API starts, health
checks work, and every request to `/graphql` without a valid token gets 401. But no token can be
valid, so the database-backed GraphQL test (create estimate → query it) can't run yet. To finish it:

1. Create an identity provider tenant, for example an Auth0 free tenant (steps in `docs/security.md`, "Using Auth0").
2. Create an API with identifier (audience) **`estimates-api`**, RS256, permissions **`estimates:read`**
   and **`estimates:write`**.
3. Put the user's tenant in a **`tenant_id`** claim of the access token (in Auth0, a post-login Action
   from `app_metadata`).
4. Set the `dev` environment variable `OIDC_AUTHORITY` to the issuer, exactly as in the token's
   `iss` (Auth0: `https://<tenant>.<region>.auth0.com/`, with the trailing slash).
5. Run **Deploy dev** manually, then call `createEstimate` and `estimates` with
   `Authorization: Bearer <token>` (see `docs/client-integration-guide.md`).

### Check your understanding

1. Why does phase 1 keep the **current** image for the API while it applies the **new** image to the
   migration task definition?
2. Why does the deploy job check that the PRIMARY deployment is the expected task definition,
   instead of trusting "services-stable"?

---

## Usage notifications and stop/start

| What | When | Where |
|---|---|---|
| Budget alerts (`graphql-demo-monthly`, US$75) | 50 %, 80 %, 100 % of actual spend; forecast above 100 % | Email from AWS Budgets |
| Daily usage report | 21:00 UTC (08:00 Sydney summer time); or **Actions → Dev cost report → Run workflow** | Email through SNS (confirm the subscription first) |
| Nightly stop | 10:00 UTC (21:00 Sydney summer time) | **Actions → Dev stop/start** run history |

**Stop or start by hand:** GitHub → **Actions → Dev stop/start → Run workflow** → choose `stop` or
`start`. From a terminal:

```bash
gh workflow run dev-power.yml --repo skmunichetty/graphql-demo -f action=stop
gh workflow run dev-power.yml --repo skmunichetty/graphql-demo -f action=start
```

- **Stop** scales the ECS service to 0 tasks, then stops the RDS instance.
- **Start** starts RDS (3–8 minutes), sets the service to 1 task, waits until it's stable, and
  checks `/health/ready`.
- Any **deployment** also starts a stopped environment automatically.

| State | Approximate cost |
|---|---|
| Running | ≈ US$2.10/day |
| Stopped | ≈ US$1.00/day: ALB ($0.60), two ALB public IPv4 addresses ($0.24), RDS storage ($0.09), secrets and logs |
| Destroyed (stage 6) | ≈ $0 (bootstrap: cents per month) |

Things to know:

- AWS **restarts a stopped RDS instance after 7 days**. The nightly stop puts it back to sleep.
- GitHub disables scheduled workflows in repositories with no activity for 60 days. The budget
  emails keep working regardless.
- Each Cost Explorer API request costs US$0.01. The daily report makes four, about US$1.20/month.
- Cost data lags by up to 24 hours, and tag-based figures start only after the `Project` tag became active.

---

## Stage 5 — a small change becomes a new task definition revision (preview)

Change something visible, open a pull request, merge it, and watch:

1. **Actions → Deploy dev**: phase 1 shows `0 to add, 1 to change` (the migration task definition)
   or similar; phase 2 replaces the API task definition (`-/+`) and updates the service.
2. **ECS → Task definitions → graphql-demo-dev-estimates-api**: a new revision (`:2`, `:3`…) whose
   image is the new digest.
3. **ECS → estimates-api → Deployments**: the new deployment starts one extra task (max 200 %),
   waits until it's healthy in the target group, drains the old one (30 s), then stops it.

## Stage 6 — rollback and cleanup (preview)

### Roll back to an earlier image

Each deployment summary shows the *previous digest*. Redeploy it without rebuilding:

```bash
gh workflow run deploy-dev.yml --repo skmunichetty/graphql-demo -f image_digest=sha256:<previous digest>
```

The rollback still runs the migration step, but with the older image, whose migrations are all
already applied. EF Core skips applied migrations and never reverts newer ones, so the step changes
nothing. **That's why migrations must stay backward-compatible**: the old code has to work with the
newer schema. Use expand/contract changes (add a column, deploy code that writes both, backfill,
switch reads, remove the old column in a *later* release). Never drop or rename in the same release
that stops using something.

ECS also rolls back automatically **during** a rollout if new tasks keep failing (deployment circuit
breaker). The deploy job then fails with "ECS rolled back to …", so you notice.

### Clean up

Destroy the dev stack (stops nearly all cost). A final DB snapshot is kept by default:

```powershell
$bucket = terraform -chdir=infra/bootstrap output -raw state_bucket
terraform -chdir=infra/dev init "-backend-config=bucket=$bucket"
$d = terraform -chdir=infra/dev output -raw api_image_digest
terraform -chdir=infra/dev destroy "-var=api_image_digest=$d" "-var=migration_image_digest=$d" `
  "-var=domain_name=estimates-dev.amapi.click" "-var=route53_zone_name=amapi.click" `
  "-var=oidc_authority=https://oidc-not-configured.invalid/"
```

- **Database data.** With the default `db_skip_final_snapshot = false`, a snapshot
  `graphql-demo-dev-estimates-final` remains and is billed as RDS backup storage (see the RDS pricing page; the amount is per GB-month of snapshot data). Delete it when
  you no longer need it: `aws rds delete-db-snapshot --db-snapshot-identifier graphql-demo-dev-estimates-final`.
  To destroy with no snapshot, add `-var=db_skip_final_snapshot=true` (the data is then gone for good).
  If a snapshot with that name already exists, delete it first or the next destroy fails.
- **The secret** is deleted immediately (`recovery_window_in_days = 0`), so a later apply can reuse the name.
- **ECR images**: `aws ecr list-images --repository-name graphql-demo/estimates-api`, then
  `aws ecr batch-delete-image --repository-name graphql-demo/estimates-api --image-ids imageDigest=sha256:…`.
  Or set `ecr_force_delete = true` in bootstrap and apply before destroying bootstrap.
- **Bootstrap**: usually keep it (cents per month). To remove everything, first confirm the dev
  stack is destroyed, then:
  1. Delete the images (above).
  2. Remove the `prevent_destroy` block from `state.tf`.
  3. Move the state back to local (`backend_override.tf` with `backend "local" {}`, then
     `terraform init -migrate-state`).
  4. Run `terraform destroy`.
  5. Empty the versioned bucket, including all object versions (S3 console → *Empty*), and delete it.

  The state bucket is versioned, so old state versions remain until the bucket is emptied.
- **GitHub**: delete the `dev` environment (Settings → Environments) and its variables.
