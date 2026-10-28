# Read by the deployment and stop/start workflows. None of these are secrets.

output "api_url" {
  value = "https://${var.domain_name}"
}

output "alb_dns_name" {
  value = aws_lb.api.dns_name
}

output "cluster_name" {
  value = aws_ecs_cluster.main.name
}

output "service_name" {
  value = aws_ecs_service.api.name
}

output "api_task_definition_arn" {
  value = aws_ecs_task_definition.api.arn
}

output "migration_task_definition_arn" {
  value = aws_ecs_task_definition.migrate.arn
}

# The digest the API service currently runs; the workflow keeps it while migrations run.
output "api_image_digest" {
  value = var.api_image_digest
}

output "task_subnet_ids" {
  value = aws_subnet.public[*].id
}

output "task_security_group_id" {
  value = aws_security_group.api.id
}

output "db_instance_identifier" {
  value = aws_db_instance.estimates.identifier
}

output "log_group_name" {
  value = aws_cloudwatch_log_group.estimates_api.name
}

output "target_group_arn" {
  value = aws_lb_target_group.api.arn
}
