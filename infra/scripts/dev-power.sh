#!/usr/bin/env bash
# Stops or starts the dev environment to save money, without destroying anything.
#   stop : ECS service -> 0 tasks, then stop the RDS instance
#   start: start RDS and wait, then ECS service -> 1 task and wait until healthy
# While stopped you still pay for the ALB, its public IPv4 addresses, database storage, secrets
# and logs (about US$1/day). AWS automatically starts a stopped RDS instance after 7 days; the
# nightly scheduled stop puts it back to sleep.
#
# Required environment variables: ACTION (stop|start), CLUSTER, SERVICE, DB_IDENTIFIER
# Optional: API_URL (health check after start)
set -euo pipefail

: "${ACTION:?}" "${CLUSTER:?}" "${SERVICE:?}" "${DB_IDENTIFIER:?}"
if [[ "$ACTION" != "stop" && "$ACTION" != "start" ]]; then
  echo "::error::ACTION must be 'stop' or 'start', got '$ACTION'"
  exit 1
fi

service_desired() {
  aws ecs describe-services --cluster "$CLUSTER" --services "$SERVICE" \
    --query 'services[?status==`ACTIVE`] | [0].desiredCount' --output text 2>/dev/null || echo "None"
}

db_status() {
  aws rds describe-db-instances --db-instance-identifier "$DB_IDENTIFIER" \
    --query 'DBInstances[0].DBInstanceStatus' --output text 2>/dev/null || echo "missing"
}

desired=$(service_desired)
status=$(db_status)
echo "Before: ECS desired tasks = $desired, RDS status = $status"

if [[ "$desired" == "None" && "$status" == "missing" ]]; then
  echo "The dev stack does not exist (destroyed?). Nothing to do."
  exit 0
fi

case "$ACTION" in
  stop)
    if [[ "$desired" != "None" && "$desired" != "0" ]]; then
      aws ecs update-service --cluster "$CLUSTER" --service "$SERVICE" --desired-count 0 > /dev/null
      echo "Scaling ECS service to 0 tasks..."
      aws ecs wait services-stable --cluster "$CLUSTER" --services "$SERVICE"
    fi
    case "$status" in
      available)
        aws rds stop-db-instance --db-instance-identifier "$DB_IDENTIFIER" > /dev/null
        echo "Stopping RDS instance (AWS restarts it automatically after 7 days if left stopped)." ;;
      stopped | stopping) echo "RDS already $status." ;;
      *) echo "::warning::RDS is '$status'; not stopping it now. The next scheduled run will try again." ;;
    esac
    ;;

  start)
    case "$status" in
      stopped) aws rds start-db-instance --db-instance-identifier "$DB_IDENTIFIER" > /dev/null ;;
      stopping) echo "RDS is still stopping; waiting before starting it again."
                aws rds wait db-instance-stopped --db-instance-identifier "$DB_IDENTIFIER"
                aws rds start-db-instance --db-instance-identifier "$DB_IDENTIFIER" > /dev/null ;;
    esac
    echo "Waiting for RDS to be available (usually 3-8 minutes from stopped)..."
    aws rds wait db-instance-available --db-instance-identifier "$DB_IDENTIFIER"

    if [[ "$desired" == "0" ]]; then
      aws ecs update-service --cluster "$CLUSTER" --service "$SERVICE" --desired-count 1 > /dev/null
    fi
    echo "Waiting for the ECS service to be stable..."
    aws ecs wait services-stable --cluster "$CLUSTER" --services "$SERVICE"

    if [[ -n "${API_URL:-}" ]]; then
      for attempt in $(seq 1 12); do
        code=$(curl -s -o /dev/null -w '%{http_code}' "$API_URL/health/ready" || true)
        [[ "$code" == "200" ]] && break
        echo "Attempt $attempt: /health/ready returned ${code:-no response}; retrying in 10 s"; sleep 10
      done
      [[ "$code" == "200" ]] || { echo "::error::$API_URL/health/ready did not return 200 after start"; exit 1; }
      echo "$API_URL/health/ready -> 200"
    fi
    ;;
esac

echo "After: ECS desired tasks = $(service_desired), RDS status = $(db_status)"
