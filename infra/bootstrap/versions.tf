terraform {
  # 1.11+ is required for write-only arguments, used in infra/dev to keep the database password
  # out of Terraform state.
  required_version = ">= 1.11"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.70"
    }
  }

  # Partial configuration: the bucket name is account-specific and this repository is public,
  # so it is passed at init time:
  #   terraform init -backend-config="bucket=<state bucket>"
  # The very first apply runs with a local backend (backend_override.tf) because this stack
  # creates the bucket. See docs/ecs-fargate-practical.md, Stage 2.
  backend "s3" {
    key          = "bootstrap/terraform.tfstate"
    region       = "ap-southeast-2"
    encrypt      = true
    use_lockfile = true # S3-native state locking; no DynamoDB table needed
  }
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project     = var.project
      Environment = var.environment
      ManagedBy   = "Terraform"
      Stack       = "bootstrap"
    }
  }
}
