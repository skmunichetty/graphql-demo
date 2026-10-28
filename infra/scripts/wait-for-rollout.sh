#!/usr/bin/env bash
# Waits for the ECS service to become stable, then checks that the deployment that finished is
# the one we expected. With the deployment circuit breaker enabled, a failing rollout is rolled
# back automatically; "stable" alone would hide that, so the task definition is compared too.
#
# Required environment variables: CLUSTER, SERVICE, EXPECTED_TASK_DEFINITION
set -euo pipefail

: "${CLUSTER:?}" "${SERVICE:?}" "${EXPECTED_TASK_DEFINITION:?}"

show_events() {
  echo "---- latest service events ----"
  aws ecs describe-services --cluster "$CLUSTER" --services "$SERVICE" \
    --query 'services[0].events[:10].[createdAt, message]' --output text || true
}

echo "Waiting for $SERVICE to become stable..."
if ! aws ecs wait services-stable --cluster "$CLUSTER" --services "$SERVICE"; then
  echo "::error::Service did not become stable within 10 minutes."
  show_events
  exit 1
fi

primary=$(aws ecs describe-services --cluster "$CLUSTER" --services "$SERVICE" \
  --query 'services[0].deployments[?status==`PRIMARY`] | [0].{taskDefinition: taskDefinition, rolloutState: rolloutState, running: runningCount, desired: desiredCount}' \
  --output json)
echo "Primary deployment: $(jq -c . <<<"$primary")"

if [[ "$(jq -r .taskDefinition <<<"$primary")" != "$EXPECTED_TASK_DEFINITION" ]]; then
  echo "::error::The rollout of ${EXPECTED_TASK_DEFINITION##*/} did not complete; ECS rolled back to $(jq -r .taskDefinition <<<"$primary" | sed 's#.*/##')."
  show_events
  exit 1
fi
if [[ "$(jq -r .rolloutState <<<"$primary")" != "COMPLETED" ]]; then
  echo "::error::Rollout state is $(jq -r .rolloutState <<<"$primary"), expected COMPLETED."
  show_events
  exit 1
fi
echo "Rollout of ${EXPECTED_TASK_DEFINITION##*/} completed."
