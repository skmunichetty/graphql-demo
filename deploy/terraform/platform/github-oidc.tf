# GitHub Actions assumes these roles through OIDC: no AWS keys are stored in GitHub.
# The account's GitHub OIDC provider already exists, so it is referenced rather than created.

data "aws_iam_openid_connect_provider" "github" {
  url = "https://token.actions.githubusercontent.com"
}

locals {
  github_oidc = "token.actions.githubusercontent.com"
}

# ---- CI: push images to ECR, only from main and version tags ----

data "aws_iam_policy_document" "github_ci_trust" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [data.aws_iam_openid_connect_provider.github.arn]
    }
    condition {
      test     = "StringEquals"
      variable = "${local.github_oidc}:aud"
      values   = ["sts.amazonaws.com"]
    }
    condition {
      test     = "StringLike"
      variable = "${local.github_oidc}:sub"
      values = [
        "repo:${var.github_repository}:ref:refs/heads/main",
        "repo:${var.github_repository}:ref:refs/tags/v*",
      ]
    }
  }
}

resource "aws_iam_role" "github_ci" {
  name                 = "${var.cluster_name}-github-ci"
  assume_role_policy   = data.aws_iam_policy_document.github_ci_trust.json
  max_session_duration = 3600
}

data "aws_iam_policy_document" "github_ci" {
  statement {
    sid       = "EcrLogin"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }
  statement {
    sid = "EcrPush"
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:BatchGetImage",
      "ecr:CompleteLayerUpload",
      "ecr:DescribeImages",
      "ecr:GetDownloadUrlForLayer",
      "ecr:InitiateLayerUpload",
      "ecr:PutImage",
      "ecr:UploadLayerPart",
    ]
    resources = [for repo in aws_ecr_repository.app : repo.arn]
  }
}

resource "aws_iam_role_policy" "github_ci" {
  name   = "ecr-push"
  role   = aws_iam_role.github_ci.id
  policy = data.aws_iam_policy_document.github_ci.json
}

# ---- CD: deploy to EKS, only from GitHub deployment environments ----
# Kubernetes permissions come from the EKS access entry in main.tf (application namespaces only).

data "aws_iam_policy_document" "github_cd_trust" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [data.aws_iam_openid_connect_provider.github.arn]
    }
    condition {
      test     = "StringEquals"
      variable = "${local.github_oidc}:aud"
      values   = ["sts.amazonaws.com"]
    }
    condition {
      test     = "StringLike"
      variable = "${local.github_oidc}:sub"
      values   = ["repo:${var.github_repository}:environment:*"]
    }
  }
}

resource "aws_iam_role" "github_cd" {
  name                 = "${var.cluster_name}-github-cd"
  assume_role_policy   = data.aws_iam_policy_document.github_cd_trust.json
  max_session_duration = 3600
}

data "aws_iam_policy_document" "github_cd" {
  statement {
    sid       = "ReadCluster"
    actions   = ["eks:DescribeCluster"]
    resources = [module.eks.cluster_arn]
  }
}

resource "aws_iam_role_policy" "github_cd" {
  name   = "eks-describe"
  role   = aws_iam_role.github_cd.id
  policy = data.aws_iam_policy_document.github_cd.json
}
