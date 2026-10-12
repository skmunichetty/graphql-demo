# EKS in the account's default VPC. Its subnets are public and there is no NAT, so nodes get
# public IPs for outbound access (ECR, AWS APIs). Security groups allow no inbound traffic from
# the internet; only the control plane reaches the nodes. Suitable for a learning environment.

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
  filter {
    name   = "default-for-az"
    values = ["true"]
  }
}

module "eks" {
  source  = "terraform-aws-modules/eks/aws"
  version = "21.29.0"

  name               = var.cluster_name
  kubernetes_version = var.kubernetes_version
  vpc_id             = data.aws_vpc.default.id
  subnet_ids         = data.aws_subnets.default.ids

  endpoint_private_access      = true
  endpoint_public_access       = true
  endpoint_public_access_cidrs = var.admin_cidrs

  # Control-plane logs (api, audit, authenticator); short retention keeps CloudWatch cost low.
  cloudwatch_log_group_retention_in_days = 7

  # The identity running Terraform becomes cluster admin.
  enable_cluster_creator_admin_permissions = true

  addons = {
    vpc-cni                = { before_compute = true }
    eks-pod-identity-agent = { before_compute = true }
    kube-proxy             = {}
    coredns                = {}
    metrics-server         = {}
  }

  eks_managed_node_groups = {
    default = {
      instance_types = var.node_instance_types
      ami_type       = "AL2023_x86_64_STANDARD"
      subnet_ids     = data.aws_subnets.default.ids
      min_size       = 1
      max_size       = 3
      desired_size   = var.node_desired_size
    }
  }

  # The CD role may only manage resources inside the application namespaces.
  access_entries = {
    github_cd = {
      principal_arn = aws_iam_role.github_cd.arn
      policy_associations = {
        admin = {
          policy_arn = "arn:aws:eks::aws:cluster-access-policy/AmazonEKSAdminPolicy"
          access_scope = {
            type       = "namespace"
            namespaces = var.app_namespaces
          }
        }
      }
    }
  }
}
