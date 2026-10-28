# Usage notifications.
# - A monthly AWS Budget for everything tagged Project=<project>: emails at 50%, 80% and 100% of
#   actual spend, and when the month's forecast passes 100%.
# - An SNS topic (email subscription) that the daily cost report workflow publishes to.
# The email address comes from terraform.tfvars, which is gitignored: this repository is public.

resource "aws_sns_topic" "alerts" {
  name = "${local.name_prefix}-alerts"
}

# AWS sends a confirmation email; the subscription stays "PendingConfirmation" until you click it.
resource "aws_sns_topic_subscription" "alerts_email" {
  topic_arn = aws_sns_topic.alerts.arn
  protocol  = "email"
  endpoint  = var.alert_email
}

# Per-project costs need the "Project" tag activated as a cost allocation tag (an account-level
# billing setting). AWS applies it to costs from activation onward, and it can take up to 24 hours.
resource "aws_ce_cost_allocation_tag" "project" {
  tag_key = "Project"
  status  = "Active"
}

resource "aws_budgets_budget" "project" {
  name         = "${var.project}-monthly"
  budget_type  = "COST"
  limit_amount = tostring(var.monthly_budget_usd)
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  cost_filter {
    name   = "TagKeyValue"
    values = ["user:Project$${var.project}"]
  }

  dynamic "notification" {
    for_each = [50, 80, 100]
    content {
      comparison_operator        = "GREATER_THAN"
      threshold                  = notification.value
      threshold_type             = "PERCENTAGE"
      notification_type          = "ACTUAL"
      subscriber_email_addresses = [var.alert_email]
    }
  }

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    notification_type          = "FORECASTED"
    subscriber_email_addresses = [var.alert_email]
  }

  depends_on = [aws_ce_cost_allocation_tag.project]
}
