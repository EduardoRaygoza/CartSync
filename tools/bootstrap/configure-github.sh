#!/usr/bin/env bash
set -euo pipefail

repo="${1:-EduardoRaygoza/CartSync}"
user_id="$(gh api user --jq .id)"

jq -n '{wait_timer:0,deployment_branch_policy:{protected_branches:false,custom_branch_policies:true}}' \
  | gh api --method PUT "repos/${repo}/environments/staging" --input -
jq -n --argjson user_id "$user_id" \
  '{wait_timer:0,prevent_self_review:false,reviewers:[{type:"User",id:$user_id}],deployment_branch_policy:{protected_branches:false,custom_branch_policies:true}}' \
  | gh api --method PUT "repos/${repo}/environments/production" --input -

for pair in staging:staging production:main; do
  environment="${pair%%:*}"
  branch="${pair##*:}"
  if ! gh api "repos/${repo}/environments/${environment}/deployment-branch-policies" \
    --jq ".branch_policies[] | select(.name == \"${branch}\") | .name" | grep -qx "$branch"; then
    gh api --method POST "repos/${repo}/environments/${environment}/deployment-branch-policies" \
      -f name="$branch" -f type=branch
  fi
done

for branch in staging main; do
  current="$(gh api "repos/${repo}/branches/${branch}/protection")"
  jq -n --argjson current "$current" '{
    required_status_checks:{strict:true,contexts:["CI / required"]},
    enforce_admins:$current.enforce_admins.enabled,
    required_pull_request_reviews:{
      dismiss_stale_reviews:$current.required_pull_request_reviews.dismiss_stale_reviews,
      require_code_owner_reviews:$current.required_pull_request_reviews.require_code_owner_reviews,
      require_last_push_approval:$current.required_pull_request_reviews.require_last_push_approval,
      required_approving_review_count:$current.required_pull_request_reviews.required_approving_review_count
    },
    restrictions:null,
    required_linear_history:$current.required_linear_history.enabled,
    allow_force_pushes:$current.allow_force_pushes.enabled,
    allow_deletions:$current.allow_deletions.enabled,
    block_creations:$current.block_creations.enabled,
    required_conversation_resolution:$current.required_conversation_resolution.enabled,
    lock_branch:$current.lock_branch.enabled,
    allow_fork_syncing:$current.allow_fork_syncing.enabled
  }' | gh api --method PUT "repos/${repo}/branches/${branch}/protection" --input -
done

printf 'GitHub environments exist and CI / required is strict on staging and main.\n'
