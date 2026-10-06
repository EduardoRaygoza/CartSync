#!/usr/bin/env bash
set -euo pipefail

repo="${1:-EduardoRaygoza/CartSync}"
user_id="$(gh api user --jq .id)"

jq -n '{wait_timer:0,prevent_self_review:false,deployment_branch_policy:{protected_branches:false,custom_branch_policies:true}}' \
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
  gh api --method PATCH "repos/${repo}/branches/${branch}/protection/required_status_checks" \
    -F strict=true \
    -f 'contexts[]=CI / required'
done

printf 'GitHub environments exist and CI / required is strict on staging and main.\n'
