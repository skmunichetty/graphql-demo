# Image repository for the Estimates API. It lives in bootstrap (not dev) because the first image
# must be pushed BEFORE the dev stack can create a task definition that references it.

resource "aws_ecr_repository" "estimates_api" {
  name = "${var.project}/estimates-api"

  # A tag can never be moved to a different image, so a commit SHA always means the same bytes.
  # Deployments go further and reference the image digest (sha256:...).
  image_tag_mutability = "IMMUTABLE"
  force_delete         = var.ecr_force_delete

  image_scanning_configuration {
    scan_on_push = true
  }

  encryption_configuration {
    encryption_type = "AES256"
  }
}

resource "aws_ecr_lifecycle_policy" "estimates_api" {
  repository = aws_ecr_repository.estimates_api.name
  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Remove untagged images after 1 day"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 1
        }
        action = { type = "expire" }
      },
      {
        # Keeps enough history to roll back several deployments.
        rulePriority = 2
        description  = "Keep the 20 most recent images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = 20
        }
        action = { type = "expire" }
      },
    ]
  })
}
