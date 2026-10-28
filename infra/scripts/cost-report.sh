#!/usr/bin/env bash
# Emails a short daily usage report through the alerts SNS topic:
#   - this project's cost yesterday and month to date, by service (tag Project=<PROJECT>)
#   - forecast for the month and the budget status
#   - whether the dev service and database are running or stopped
# Cost Explorer data lags by up to 24 hours. Each Cost Explorer API request costs US$0.01
# (this report makes 4, about US$1.20/month).
#
# Required environment variables: PROJECT, TOPIC_ARN, CLUSTER, SERVICE, DB_IDENTIFIER, BUDGET_NAME
set -euo pipefail

: "${PROJECT:?}" "${TOPIC_ARN:?}" "${CLUSTER:?}" "${SERVICE:?}" "${DB_IDENTIFIER:?}" "${BUDGET_NAME:?}"

ce() { aws ce "$@" --region us-east-1 --output json; } # Cost Explorer is a global (us-east-1) API

today=$(date -u +%F)
yesterday=$(date -u -d yesterday +%F)
month_start=$(date -u -d "$yesterday" +%Y-%m-01)          # month containing yesterday
next_month=$(date -u -d "$month_start +1 month" +%F)
tag_filter=$(jq -cn --arg p "$PROJECT" '{Tags: {Key: "Project", Values: [$p]}}')
money() { awk '{ printf "US$%.2f", $1 }'; }

month=$(ce get-cost-and-usage --time-period "Start=$month_start,End=$today" --granularity MONTHLY \
  --metrics UnblendedCost --filter "$tag_filter" --group-by Type=DIMENSION,Key=SERVICE)
month_total=$(jq '[.ResultsByTime[].Groups[].Metrics.UnblendedCost.Amount | tonumber] | add // 0' <<<"$month")
by_service=$(jq -r '[.ResultsByTime[].Groups[] | {s: .Keys[0], a: (.Metrics.UnblendedCost.Amount | tonumber)}]
  | sort_by(-.a) | map(select(.a >= 0.005)) | .[] | "  \(.s): US$\(.a * 100 | round / 100)"' <<<"$month")

daily=$(ce get-cost-and-usage --time-period "Start=$yesterday,End=$today" --granularity DAILY \
  --metrics UnblendedCost --filter "$tag_filter")
yesterday_total=$(jq '[.ResultsByTime[].Total.UnblendedCost.Amount | tonumber] | add // 0' <<<"$daily")

account=$(ce get-cost-and-usage --time-period "Start=$month_start,End=$today" --granularity MONTHLY --metrics UnblendedCost)
account_total=$(jq '[.ResultsByTime[].Total.UnblendedCost.Amount | tonumber] | add // 0' <<<"$account")

# Forecasts need some history; early in a month or right after tag activation this can fail.
forecast_text="not available yet"
if [[ "$today" < "$next_month" ]] && forecast=$(ce get-cost-forecast --time-period "Start=$today,End=$next_month" \
     --granularity MONTHLY --metric UNBLENDED_COST --filter "$tag_filter" 2>/dev/null); then
  forecast_text="$(jq -r '.Total.Amount' <<<"$forecast" | money) more by $(date -u -d "$next_month -1 day" +%F)"
fi

account_id=$(aws sts get-caller-identity --query Account --output text)
budget_text=$(aws budgets describe-budget --account-id "$account_id" --budget-name "$BUDGET_NAME" \
  --query 'Budget.[CalculatedSpend.ActualSpend.Amount, BudgetLimit.Amount]' --output text 2>/dev/null \
  | awk '{ printf "US$%.2f of US$%.2f (%.0f%%)", $1, $2, ($2 > 0 ? 100 * $1 / $2 : 0) }' || echo "unavailable")

service_state=$(aws ecs describe-services --cluster "$CLUSTER" --services "$SERVICE" \
  --query 'services[?status==`ACTIVE`] | [0].[desiredCount, runningCount]' --output text 2>/dev/null || echo "None")
db_state=$(aws rds describe-db-instances --db-instance-identifier "$DB_IDENTIFIER" \
  --query 'DBInstances[0].DBInstanceStatus' --output text 2>/dev/null || echo "not found")

if [[ "$service_state" == "None" || -z "$service_state" ]]; then
  running="dev stack not deployed (or destroyed)"
else
  read -r desired running_count <<<"$service_state"
  running="ECS tasks: $running_count running / $desired desired; RDS: $db_state"
fi

message=$(cat <<EOF
$PROJECT dev - daily AWS usage report ($today, UTC)

Project cost (tag Project=$PROJECT)
  Yesterday ($yesterday): $(money <<<"$yesterday_total")
  Month to date:          $(money <<<"$month_total")
  Forecast:               $forecast_text
  Budget ($BUDGET_NAME):  $budget_text

By service, month to date:
${by_service:-  (no tagged costs yet - tag data can take up to 24 h after activation)}

Whole account, month to date (all projects): $(money <<<"$account_total")

Current state: $running

Save money: GitHub -> Actions -> "Dev stop/start" -> Run workflow -> stop.
Stopped, dev still costs about US\$1/day (load balancer, public IPv4, storage). Destroy it to pay ~\$0.
EOF
)

echo "$message"
aws sns publish --topic-arn "$TOPIC_ARN" --subject "$PROJECT dev usage $today: $(money <<<"$month_total") this month" \
  --message "$message" > /dev/null
echo "Report published to SNS."
