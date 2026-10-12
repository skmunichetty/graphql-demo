# AWS infrastructure (Terraform)

Learning-grade AWS foundation for deploying the services to EKS (ap-southeast-2).

| Stack | Creates | State |
|---|---|---|
| `bootstrap/` | S3 bucket for Terraform state (versioned, encrypted, private, TLS-only) | Local, run once |
| `platform/` | EKS cluster + managed node group, ECR repositories, GitHub Actions IAM roles | S3, locked with `use_lockfile` |

## What platform/ creates

- **EKS** (`graphql-demo`, Kubernetes 1.36) in the account's **default VPC**. Its subnets are
  public and have no NAT, so nodes get public IPs for outbound traffic. Node security groups only
  accept traffic from the control plane and other nodes. The API endpoint is public but limited to
  `admin_cidrs`, and also reachable privately inside the VPC.
- **Node group**: 2 × t3.medium (min 1, max 3), Amazon Linux 2023.
- **ECR** `estimates-api`: immutable tags, scan on push, untagged images expire after 7 days,
  and only the 30 most recent images are kept.
- **GitHub OIDC roles** for `skmunichetty/graphql-demo` (no AWS keys stored in GitHub):
  - `graphql-demo-github-ci`: push to ECR. Only `main` and `v*` tags can assume it.
  - `graphql-demo-github-cd`: `eks:DescribeCluster`, plus Kubernetes admin **only in the
    `estimates` namespace** (EKS access entry). Only GitHub deployment environments can assume it.

Approximate cost while running: about US$160/month. That covers the EKS control plane (~$73),
2 × t3.medium (~$77), public IPv4 addresses, EBS and logs. Destroy it when you're not using it.

## First-time setup

```bash
terraform -chdir=bootstrap init
terraform -chdir=bootstrap apply

cp platform/terraform.tfvars.example platform/terraform.tfvars   # set admin_cidrs to your IP/32
terraform -chdir=platform init
terraform -chdir=platform plan -out=platform.tfplan
terraform -chdir=platform apply platform.tfplan
```

Then connect kubectl and create the application namespace. The CD role cannot create namespaces:

```bash
aws eks update-kubeconfig --name graphql-demo --region ap-southeast-2
kubectl create namespace estimates
```

## Limitations and known gaps

- **GitHub-hosted runners cannot reach the EKS API** while `admin_cidrs` is limited to your IP.
  Milestone 8 (CD) must either allow the runner IP ranges, use a self-hosted runner in the VPC,
  or open the endpoint (IAM still controls access).
- Public-subnet nodes are a cost trade-off. For production use private subnets with NAT.
- No Karpenter or AWS Load Balancer Controller yet. The load balancer controller comes in milestone 9.

## Tear down

```bash
terraform -chdir=platform destroy
```

ECR repositories that still contain images aren't deleted (`force_delete = false`). The state
bucket has `prevent_destroy` set and is kept.
