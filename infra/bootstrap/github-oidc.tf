# GitHub Actions authenticates to AWS with short-lived OIDC tokens; no AWS keys are stored in GitHub.
#
# The account's GitHub OIDC identity provider already exists and other projects may rely on it,
# so it is READ here, not owned. (If Terraform owned it, destroying this stack would break them.)
# In a fresh account, create it once with aws_iam_openid_connect_provider.
data "aws_iam_openid_connect_provider" "github" {
  url = "https://token.actions.githubusercontent.com"
}

locals {
  name_prefix  = "${var.project}-${var.environment}" # graphql-demo-dev
  oidc_host    = "token.actions.githubusercontent.com"
  oidc_subject = "repo:${var.github_repository}:environment:${var.github_environment}"

  # Names the dev stack uses; the roles below are scoped to them.
  cluster_name  = local.name_prefix
  service_name  = "estimates-api"
  db_identifier = "${local.name_prefix}-estimates"
  role_prefix   = "${local.name_prefix}-"
}

data "aws_route53_zone" "public" {
  name         = var.route53_zone_name
  private_zone = false
}

# Trust: only jobs running in this repository's "dev" GitHub environment. A job's OIDC "sub" claim
# is "repo:<owner>/<repo>:environment:<name>" when it declares an environment. Pull-request
# workflows cannot get these credentials, because the environment only allows the main branch.
data "aws_iam_policy_document" "github_trust" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [data.aws_iam_openid_connect_provider.github.arn]
    }
    condition {
      test     = "StringEquals"
      variable = "${local.oidc_host}:aud"
      values   = ["sts.amazonaws.com"]
    }
    condition {
      test     = "StringEquals"
      variable = "${local.oidc_host}:sub"
      values   = [local.oidc_subject]
    }
  }
}

# ---------------------------------------------------------------------------------------------
# Permissions boundary: the MOST any role created by the deploy role can ever do.
# Without it, a deploy role that can create IAM roles could create an administrator role and
# escalate. The deploy role may only create or change roles that carry this boundary.
# ---------------------------------------------------------------------------------------------
data "aws_iam_policy_document" "workload_boundary" {
  statement {
    sid       = "PullImages"
    actions   = ["ecr:GetAuthorizationToken", "ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"]
    resources = ["*"]
  }
  statement {
    sid       = "WriteLogs"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["arn:aws:logs:${var.region}:${local.account_id}:log-group:/ecs/${local.name_prefix}/*"]
  }
  statement {
    sid       = "ReadOwnSecrets"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = ["arn:aws:secretsmanager:${var.region}:${local.account_id}:secret:${var.project}/${var.environment}/*"]
  }
  statement {
    sid       = "EcsExecLater"
    actions   = ["ssmmessages:CreateControlChannel", "ssmmessages:CreateDataChannel", "ssmmessages:OpenControlChannel", "ssmmessages:OpenDataChannel"]
    resources = ["*"]
  }
}

resource "aws_iam_policy" "workload_boundary" {
  name        = "${local.name_prefix}-workload-boundary"
  description = "Maximum permissions for ECS task and execution roles created by the GitHub deploy role."
  policy      = data.aws_iam_policy_document.workload_boundary.json
}

# ---------------------------------------------------------------------------------------------
# Deploy role: runs Terraform for infra/dev, pushes images, runs the migration task.
# ---------------------------------------------------------------------------------------------
resource "aws_iam_role" "github_deploy" {
  name                 = "${local.name_prefix}-github-deploy"
  description          = "Assumed by GitHub Actions (environment ${var.github_environment}) to deploy ${local.name_prefix}."
  assume_role_policy   = data.aws_iam_policy_document.github_trust.json
  max_session_duration = 3600
}

data "aws_iam_policy_document" "github_deploy" {
  statement {
    sid       = "TerraformStateList"
    actions   = ["s3:ListBucket"]
    resources = [aws_s3_bucket.state.arn]
    condition {
      test     = "StringLike"
      variable = "s3:prefix"
      values   = ["dev/*"]
    }
  }
  statement {
    sid       = "TerraformStateReadWrite"
    actions   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
    resources = ["${aws_s3_bucket.state.arn}/dev/*"]
  }
  statement {
    sid       = "EcrLogin"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }
  statement {
    sid = "EcrPushPull"
    actions = [
      "ecr:BatchCheckLayerAvailability", "ecr:BatchGetImage", "ecr:CompleteLayerUpload", "ecr:DescribeImages",
      "ecr:DescribeRepositories", "ecr:GetDownloadUrlForLayer", "ecr:InitiateLayerUpload", "ecr:PutImage", "ecr:UploadLayerPart",
    ]
    resources = [aws_ecr_repository.estimates_api.arn]
  }

  # Infrastructure services used by infra/dev, limited to this region.
  statement {
    sid = "ManageDevInfrastructure"
    actions = [
      "ec2:*", "elasticloadbalancing:*", "ecs:*", "rds:*", "logs:*", "acm:*", "cloudwatch:DescribeAlarms",
      "application-autoscaling:Describe*",
    ]
    resources = ["*"]
    condition {
      test     = "StringEquals"
      variable = "aws:RequestedRegion"
      values   = [var.region]
    }
  }
  # Cost guard-rails: this stack never needs servers or NAT gateways.
  statement {
    sid       = "DenyComputeAndNat"
    effect    = "Deny"
    actions   = ["ec2:RunInstances", "ec2:StartInstances", "ec2:RequestSpotInstances", "ec2:RequestSpotFleet", "ec2:CreateFleet", "ec2:CreateNatGateway"]
    resources = ["*"]
  }
  statement {
    sid       = "ManageDevSecrets"
    actions   = ["secretsmanager:*"]
    resources = ["arn:aws:secretsmanager:${var.region}:${local.account_id}:secret:${var.project}/${var.environment}/*"]
  }
  statement {
    sid       = "ListSecrets"
    actions   = ["secretsmanager:ListSecrets", "secretsmanager:GetRandomPassword"]
    resources = ["*"]
  }
  # RDS storage encryption with the AWS-managed key needs these, but only through RDS.
  statement {
    sid       = "KmsForRds"
    actions   = ["kms:DescribeKey", "kms:CreateGrant", "kms:ListAliases"]
    resources = ["*"]
    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values   = ["rds.${var.region}.amazonaws.com"]
    }
  }
  statement {
    sid       = "KmsListAliases"
    actions   = ["kms:ListAliases", "kms:DescribeKey"]
    resources = ["*"]
  }
  statement {
    sid       = "DnsRecordsInZone"
    actions   = ["route53:ChangeResourceRecordSets", "route53:ListResourceRecordSets", "route53:GetHostedZone", "route53:ListTagsForResource"]
    resources = [data.aws_route53_zone.public.arn]
  }
  statement {
    sid       = "DnsRead"
    actions   = ["route53:ListHostedZones", "route53:ListHostedZonesByName", "route53:GetChange"]
    resources = ["*"]
  }

  # IAM: only roles named graphql-demo-dev-*, and only WITH the permissions boundary.
  statement {
    sid       = "CreateAndChangeBoundedRoles"
    actions   = ["iam:CreateRole", "iam:PutRolePolicy", "iam:DeleteRolePolicy", "iam:AttachRolePolicy", "iam:DetachRolePolicy", "iam:PutRolePermissionsBoundary"]
    resources = ["arn:aws:iam::${local.account_id}:role/${local.role_prefix}*"]
    condition {
      test     = "StringEquals"
      variable = "iam:PermissionsBoundary"
      values   = [aws_iam_policy.workload_boundary.arn]
    }
  }
  statement {
    sid = "ManageOwnRoles"
    actions = [
      "iam:GetRole", "iam:DeleteRole", "iam:TagRole", "iam:UntagRole", "iam:UpdateRole", "iam:UpdateAssumeRolePolicy",
      "iam:GetRolePolicy", "iam:ListRolePolicies", "iam:ListAttachedRolePolicies", "iam:ListInstanceProfilesForRole",
    ]
    resources = ["arn:aws:iam::${local.account_id}:role/${local.role_prefix}*"]
  }
  statement {
    sid       = "PassTaskRolesToEcsOnly"
    actions   = ["iam:PassRole"]
    resources = ["arn:aws:iam::${local.account_id}:role/${local.role_prefix}*"]
    condition {
      test     = "StringEquals"
      variable = "iam:PassedToService"
      values   = ["ecs-tasks.amazonaws.com"]
    }
  }
  statement {
    sid       = "ServiceLinkedRoles"
    actions   = ["iam:CreateServiceLinkedRole"]
    resources = ["*"]
    condition {
      test     = "StringEquals"
      variable = "iam:AWSServiceName"
      values   = ["ecs.amazonaws.com", "elasticloadbalancing.amazonaws.com", "rds.amazonaws.com"]
    }
  }
  statement {
    sid       = "ReadIamPolicies"
    actions   = ["iam:GetPolicy", "iam:GetPolicyVersion"]
    resources = ["*"]
  }
  # The deploy role can never weaken its own guard-rails.
  statement {
    sid    = "ProtectGuardRails"
    effect = "Deny"
    actions = [
      "iam:DeleteRolePermissionsBoundary", "iam:CreatePolicyVersion", "iam:DeletePolicy", "iam:SetDefaultPolicyVersion",
    ]
    resources = ["*"]
  }
  statement {
    sid       = "ProtectBootstrapRoles"
    effect    = "Deny"
    actions   = ["iam:*"]
    resources = [aws_iam_role.github_deploy.arn, aws_iam_role.github_ops.arn]
  }
}

resource "aws_iam_role_policy" "github_deploy" {
  name   = "deploy-dev"
  role   = aws_iam_role.github_deploy.id
  policy = data.aws_iam_policy_document.github_deploy.json
}

# ---------------------------------------------------------------------------------------------
# Ops role: stop/start the dev environment and send the daily cost report. Nothing else.
# ---------------------------------------------------------------------------------------------
resource "aws_iam_role" "github_ops" {
  name                 = "${local.name_prefix}-github-ops"
  description          = "Assumed by GitHub Actions (environment ${var.github_environment}) to stop/start ${local.name_prefix} and report costs."
  assume_role_policy   = data.aws_iam_policy_document.github_trust.json
  max_session_duration = 3600
}

data "aws_iam_policy_document" "github_ops" {
  statement {
    sid       = "ScaleService"
    actions   = ["ecs:UpdateService", "ecs:DescribeServices"]
    resources = ["arn:aws:ecs:${var.region}:${local.account_id}:service/${local.cluster_name}/${local.service_name}"]
  }
  statement {
    sid       = "ReadTasks"
    actions   = ["ecs:ListTasks", "ecs:DescribeTasks", "ecs:DescribeClusters"]
    resources = ["*"]
  }
  statement {
    sid       = "StopStartDatabase"
    actions   = ["rds:StopDBInstance", "rds:StartDBInstance", "rds:DescribeDBInstances"]
    resources = ["arn:aws:rds:${var.region}:${local.account_id}:db:${local.db_identifier}"]
  }
  statement {
    sid       = "ReadLoadBalancers"
    actions   = ["elasticloadbalancing:DescribeLoadBalancers", "elasticloadbalancing:DescribeTargetGroups", "elasticloadbalancing:DescribeTargetHealth"]
    resources = ["*"]
  }
  statement {
    sid       = "ReadCosts"
    actions   = ["ce:GetCostAndUsage", "ce:GetCostForecast", "budgets:ViewBudget"]
    resources = ["*"]
  }
  statement {
    sid       = "SendReports"
    actions   = ["sns:Publish"]
    resources = [aws_sns_topic.alerts.arn]
  }
}

resource "aws_iam_role_policy" "github_ops" {
  name   = "ops-dev"
  role   = aws_iam_role.github_ops.id
  policy = data.aws_iam_policy_document.github_ops.json
}
