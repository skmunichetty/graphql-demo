terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  # Created by ../bootstrap. S3-native locking (use_lockfile) needs no DynamoDB table.
  backend "s3" {
    bucket       = "graphql-demo-tfstate-476114141817"
    key          = "platform/terraform.tfstate"
    region       = "ap-southeast-2"
    encrypt      = true
    use_lockfile = true
  }
}

provider "aws" {
  region = var.region
  default_tags {
    tags = {
      Project     = "graphql-demo"
      Environment = "learning"
      ManagedBy   = "Terraform"
    }
  }
}
