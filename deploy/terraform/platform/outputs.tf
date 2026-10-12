output "region" {
  value = var.region
}

output "cluster_name" {
  value = module.eks.cluster_name
}

output "cluster_endpoint" {
  value = module.eks.cluster_endpoint
}

output "node_security_group_id" {
  value = module.eks.node_security_group_id
}

output "ecr_repositories" {
  value = { for name, repo in aws_ecr_repository.app : name => repo.repository_url }
}

# Set these as GitHub repository variables (not secrets; they are not credentials).
output "github_ci_role_arn" {
  value = aws_iam_role.github_ci.arn
}

output "github_cd_role_arn" {
  value = aws_iam_role.github_cd.arn
}

output "kubeconfig_command" {
  value = "aws eks update-kubeconfig --name ${module.eks.cluster_name} --region ${var.region}"
}
