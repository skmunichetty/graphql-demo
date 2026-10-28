#!/usr/bin/env bash
# Runs the database migration as a one-off ECS task (like a Kubernetes Job) and exits non-zero
# unless the container exited with code 0.
#
# Required environment variables (the deploy workflow takes them from `terraform output`):
#   CLUSTER, TASK_DEFINITION, SUBNETS (comma-separated), SECURITY_GROUP, LOG_GROUP
set -euo pipefail

: "${CLUSTER:?}" "${TASK_DEFINITION:?}" "${SUBNETS:?}" "${SECURITY_GROUP:?}" "${LOG_GROUP:?}"

echo "Starting migration task from ${TASK_DEFINITION##*/}"
run_output=$(aws ecs run-task \
  --cluster "$CLUSTER" \
  --task-definition "$TASK_DEFINITION" \
  --launch-type FARGATE \
  --count 1 \
  --started-by "migrate-${GITHUB_RUN_ID:-manual}" \
  --network-configuration "awsvpcConfiguration={subnets=[${SUBNETS}],securityGroups=[${SECURITY_GROUP}],assignPublicIp=ENABLED}" \
  --output json)

task_arn=$(jq -r '.tasks[0].taskArn // empty' <<<"$run_output")
if [[ -z "$task_arn" ]]; then
  echo "::error::ECS could not start the migration task: $(jq -c '.failures' <<<"$run_output")"
  exit 1
fi
task_id="${task_arn##*/}"
echo "Migration task $task_id started; waiting for it to stop (pull image, connect, migrate)..."

# Polls every 6 s for up to 10 minutes.
aws ecs wait tasks-stopped --cluster "$CLUSTER" --tasks "$task_arn"

result=$(aws ecs describe-tasks --cluster "$CLUSTER" --tasks "$task_arn" \
  --query 'tasks[0].{exitCode: containers[0].exitCode, reason: containers[0].reason, stopCode: stopCode, stoppedReason: stoppedReason}' \
  --output json)
exit_code=$(jq -r '.exitCode // "none"' <<<"$result")

# The migrator logs JSON lines; print just the messages. (Connection strings are never logged.)
echo "---- migration log ($LOG_GROUP, stream migrate/migrate/$task_id) ----"
aws logs get-log-events --log-group-name "$LOG_GROUP" --log-stream-name "migrate/migrate/$task_id" \
  --start-from-head --query 'events[].message' --output json 2>/dev/null \
  | jq -r '.[] | (fromjson? // {Message: .}) | "\(.LogLevel // "")  \(.Message)"' || echo "(no log events found)"
echo "---------------------------------------------------------------"

if [[ "$exit_code" != "0" ]]; then
  echo "::error::Migration task failed (exit code: $exit_code). Details: $(jq -c . <<<"$result")"
  exit 1
fi
echo "Migration task completed successfully (exit code 0)."
