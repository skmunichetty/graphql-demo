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

# ---- Images (set by the deployment workflow) ----

variable "api_image_digest" {
  type        = string
  description = "Image digest (sha256:...) the API service runs. The workflow keeps the current one during migrations."

  validation {
    condition     = can(regex("^sha256:[a-f0-9]{64}$", var.api_image_digest))
    error_message = "api_image_digest must look like sha256:<64 hex characters>."
  }
}

variable "migration_image_digest" {
  type        = string
  description = "Image digest (sha256:...) the one-off migration task runs. Normally the image being deployed."

  validation {
    condition     = can(regex("^sha256:[a-f0-9]{64}$", var.migration_image_digest))
    error_message = "migration_image_digest must look like sha256:<64 hex characters>."
  }
}

# ---- Public endpoint ----

variable "domain_name" {
  type        = string
  description = "Public HTTPS hostname for the API, for example estimates-dev.example.com. Must be inside route53_zone_name."
}

variable "route53_zone_name" {
  type        = string
  description = "Existing public Route 53 hosted zone that receives the certificate validation and alias records."
}

# ---- Authentication (the API refuses to start without a valid HTTPS authority) ----

variable "oidc_authority" {
  type        = string
  description = "OIDC issuer URL; must equal the token's iss claim exactly (Auth0 issuers end with /)."

  validation {
    condition     = startswith(var.oidc_authority, "https://")
    error_message = "oidc_authority must be an HTTPS URL. Authentication is never disabled."
  }
}

variable "oidc_audience" {
  type    = string
  default = "estimates-api"
}

variable "tenant_claim_type" {
  type    = string
  default = "tenant_id"
}

# ---- Sizing ----

variable "task_cpu" {
  type        = number
  description = "Fargate CPU units (256 = 0.25 vCPU)."
  default     = 256
}

variable "task_memory" {
  type        = number
  description = "Fargate memory in MiB (512 = 0.5 GB)."
  default     = 512
}

variable "db_instance_class" {
  type    = string
  default = "db.t4g.micro"
}

variable "db_allocated_storage" {
  type    = number
  default = 20
}

variable "db_password_version" {
  type        = number
  description = "Increase by 1 to generate and apply a new database password (write-only rotation)."
  default     = 1
}

variable "db_skip_final_snapshot" {
  type        = bool
  description = "false (default): terraform destroy keeps a final snapshot of the database. true: data is gone."
  default     = false
}

variable "log_retention_days" {
  type    = number
  default = 7
}

variable "vpc_cidr" {
  type    = string
  default = "10.40.0.0/16"
}
