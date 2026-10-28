variable "region" {
  type    = string
  default = "ap-southeast-2"
}

variable "project" {
  type    = string
  default = "graphql-demo"
}

variable "environment" {
  type    = string
  default = "dev"
}

variable "github_repository" {
  type        = string
  description = "owner/name of the only repository whose workflows may assume the GitHub roles."
  default     = "skmunichetty/graphql-demo"
}

variable "github_environment" {
  type        = string
  description = "GitHub deployment environment the roles trust. Restrict it to the main branch in GitHub."
  default     = "dev"
}

variable "alert_email" {
  type        = string
  description = "Receives budget alerts and the daily cost report. Set in terraform.tfvars (gitignored)."

  validation {
    condition     = can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.alert_email))
    error_message = "alert_email must be an email address."
  }
}

variable "monthly_budget_usd" {
  type        = number
  description = "Monthly cost budget for resources tagged Project=<project>."
  default     = 75
}

variable "route53_zone_name" {
  type        = string
  description = "Existing public hosted zone the dev stack may add records to (ACM validation and the API alias)."
  default     = "amapi.click"
}

variable "ecr_force_delete" {
  type        = bool
  description = "Allow terraform destroy to delete the ECR repository even if it still contains images."
  default     = false
}
