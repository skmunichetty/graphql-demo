# None of these are secrets. They become GitHub environment variables for "dev".

output "state_bucket" {
  value = aws_s3_bucket.state.bucket
}

output "ecr_repository_url" {
  value = aws_ecr_repository.estimates_api.repository_url
}

output "github_deploy_role_arn" {
  value = aws_iam_role.github_deploy.arn
}

output "github_ops_role_arn" {
  value = aws_iam_role.github_ops.arn
}

output "workload_boundary_arn" {
  value = aws_iam_policy.workload_boundary.arn
}

output "alerts_topic_arn" {
  value = aws_sns_topic.alerts.arn
}

output "route53_zone_id" {
  value = data.aws_route53_zone.public.zone_id
}
