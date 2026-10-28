# ECS: one cluster, one service running the API, and a separate task definition for the one-off
# migration task. Terraform owns both task definitions and the service's task definition, so a
# deployment is "terraform apply with a new image digest". See docs/ecs-fargate-practical.md.

data "aws_caller_identity" "current" {}

data "aws_ecr_repository" "estimates_api" {
  name = "${var.project}/estimates-api" # created by infra/bootstrap
}

data "aws_iam_policy" "workload_boundary" {
  name = "${local.name_prefix}-workload-boundary" # created by infra/bootstrap
}

locals {
  api_image       = "${data.aws_ecr_repository.estimates_api.repository_url}@${var.api_image_digest}"
  migration_image = "${data.aws_ecr_repository.estimates_api.repository_url}@${var.migration_image_digest}"
  connection_secret = {
    name      = "ConnectionStrings__EstimatesDb"
    valueFrom = "${aws_secretsmanager_secret.estimates_db.arn}:connectionString::"
  }
}

resource "aws_ecs_cluster" "main" {
  name = local.name_prefix

  # Container Insights adds CloudWatch metric charges; plain ECS metrics are enough to learn with.
  setting {
    name  = "containerInsights"
    value = "disabled"
  }
}

resource "aws_cloudwatch_log_group" "estimates_api" {
  name              = "/ecs/${local.name_prefix}/estimates-api"
  retention_in_days = var.log_retention_days
}

# ---- IAM: two roles with different jobs ----

data "aws_iam_policy_document" "ecs_tasks_trust" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
    # Only ECS tasks in this account can use these roles (confused-deputy protection).
    condition {
      test     = "StringEquals"
      variable = "aws:SourceAccount"
      values   = [data.aws_caller_identity.current.account_id]
    }
  }
}

# EXECUTION role: used by the ECS agent BEFORE the app starts, to pull the image, create the log
# stream and read the connection-string secret. The application code never sees these credentials.
resource "aws_iam_role" "task_execution" {
  name                 = "${local.name_prefix}-estimates-execution"
  assume_role_policy   = data.aws_iam_policy_document.ecs_tasks_trust.json
  permissions_boundary = data.aws_iam_policy.workload_boundary.arn
}

resource "aws_iam_role_policy_attachment" "task_execution_managed" {
  role       = aws_iam_role.task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

data "aws_iam_policy_document" "task_execution_secret" {
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.estimates_db.arn]
  }
}

resource "aws_iam_role_policy" "task_execution_secret" {
  name   = "read-database-secret"
  role   = aws_iam_role.task_execution.id
  policy = data.aws_iam_policy_document.task_execution_secret.json
}

# TASK role: the identity of the running application. The API calls no AWS APIs today, so it has
# no permissions. Grant here (never on the execution role) anything the code itself needs later.
resource "aws_iam_role" "task" {
  name                 = "${local.name_prefix}-estimates-task"
  assume_role_policy   = data.aws_iam_policy_document.ecs_tasks_trust.json
  permissions_boundary = data.aws_iam_policy.workload_boundary.arn
}

# ---- Task definitions ----

resource "aws_ecs_task_definition" "api" {
  family                   = "${local.name_prefix}-estimates-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.task_cpu
  memory                   = var.task_memory
  execution_role_arn       = aws_iam_role.task_execution.arn
  task_role_arn            = aws_iam_role.task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64" # images are built on GitHub's x86-64 Ubuntu runners
  }

  container_definitions = jsonencode([{
    name      = "estimates-api"
    image     = local.api_image
    essential = true

    portMappings = [{ containerPort = 8080, protocol = "tcp" }]

    environment = [
      { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
      # TLS ends at the ALB. Trust its X-Forwarded-Proto/For (only the ALB can reach the task),
      # and tell the app its public HTTPS port so plain-HTTP requests are redirected.
      { name = "ASPNETCORE_FORWARDEDHEADERS_ENABLED", value = "true" },
      { name = "ASPNETCORE_HTTPS_PORT", value = "443" },
      { name = "Authentication__Authority", value = var.oidc_authority },
      { name = "Authentication__Audience", value = var.oidc_audience },
      { name = "Authentication__TenantClaimType", value = var.tenant_claim_type },
    ]
    secrets = [local.connection_secret]

    # Liveness only (no database): a database outage must not make ECS restart the task.
    healthCheck = {
      command     = ["CMD", "dotnet", "/app/Estimates.Api.dll", "--health-check"]
      interval    = 30
      timeout     = 5
      retries     = 3
      startPeriod = 30
    }

    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.estimates_api.name
        awslogs-region        = var.region
        awslogs-stream-prefix = "api"
      }
    }
  }])
}

# The same image in "migrate and exit" mode. Run once per deployment with aws ecs run-task; its
# exit code decides whether the new API version is rolled out.
resource "aws_ecs_task_definition" "migrate" {
  family                   = "${local.name_prefix}-estimates-migrate"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.task_cpu
  memory                   = var.task_memory
  execution_role_arn       = aws_iam_role.task_execution.arn
  task_role_arn            = aws_iam_role.task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([{
    name      = "migrate"
    image     = local.migration_image
    essential = true
    command   = ["--migrate-database"] # appended to the image's ENTRYPOINT ["dotnet", "Estimates.Api.dll"]

    environment = [
      { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
    ]
    secrets = [local.connection_secret]

    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.estimates_api.name
        awslogs-region        = var.region
        awslogs-stream-prefix = "migrate"
      }
    }
  }])
}

# ---- Service ----

resource "aws_ecs_service" "api" {
  name            = "estimates-api"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  launch_type     = "FARGATE"

  # Created with 0 tasks: the first deployment must run migrations before any API task starts.
  # After that the deployment workflow and the stop/start workflow own the running count, so
  # Terraform ignores it (see lifecycle below). Terraform still owns the image and configuration.
  desired_count = 0

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.api.id]
    assign_public_ip = true # outbound to ECR/CloudWatch/Secrets Manager/OIDC without a NAT gateway
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "estimates-api"
    container_port   = 8080
  }

  # Rolling update: start the new task, wait until healthy in the target group, then stop the old.
  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200
  health_check_grace_period_seconds  = 60

  # If new tasks keep failing, ECS stops the rollout and returns to the last working revision.
  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  propagate_tags        = "SERVICE"
  wait_for_steady_state = false # the workflow waits explicitly and checks the result

  lifecycle {
    ignore_changes = [desired_count]
  }

  depends_on = [aws_lb_listener.https]
}
