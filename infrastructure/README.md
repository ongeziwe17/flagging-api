# Feature Flagging Infrastructure

This directory contains the Terraform configuration for the Feature Flagging platform. It provisions secure and cost-conscious AWS infrastructure with reusable modules, remote state, and CI/CD guardrails.

## Repository layout

```
infrastructure/
├── modules/
│   ├── compute/            # Lambda + API Gateway + Secrets automation
│   └── networking/         # VPC, subnets, routing, optional flow logs
├── environments/
│   ├── dev/                # Developer sandbox deployment
│   └── prod/               # Production deployment
└── state/                  # Bootstrap for remote state storage
```

Each environment composes the shared modules, enables AWS Budgets, and configures an IAM role dedicated to Terraform execution via GitHub Actions OIDC federation.

## Remote state

Remote Terraform state is stored in an encrypted, versioned S3 bucket with DynamoDB table locking. Bootstrap the backend once per AWS account:

```bash
cd infrastructure/state
terraform init
terraform apply \
  -var "aws_region=us-east-1" \
  -var "bucket_name=feature-flagging-terraform-state" \
  -var "dynamodb_table_name=feature-flagging-terraform-locks" \
  -var "allowed_role_arns=[\"arn:aws:iam::<account-id>:role/feature-flagging-dev-terraform\",\"arn:aws:iam::<account-id>:role/feature-flagging-prod-terraform\"]"
```

### Access policy

The state bucket policy restricts access to IAM roles listed in `allowed_role_arns`. Update the list as new deployment roles are created. Server-side encryption (SSE-S3 or SSE-KMS) and versioning are enforced, and public access is fully blocked.

## Modules

### Networking

Creates a minimal-cost VPC with public subnets, routing, and optional VPC flow logs. Modules accept CIDR ranges and tagging metadata so the same module can be reused across environments.

### Compute

Deploys a Python-based Lambda function, exposes it through an HTTP API Gateway, and provisions:

- Dedicated security group restricted to egress over HTTPS
- CloudWatch logging for both Lambda and API Gateway
- Optional Secrets Manager secret containing a generated signing key
- Alarm routing via SNS (email subscription configured by environment)

The module prefers serverless components to minimize idle cost while providing hooks for expanding functionality.

## Environments

Each environment directory (`dev`, `prod`) defines:

- Provider configuration and backend linkage
- Module composition with environment-specific CIDR ranges and tagging
- AWS Budgets with ACTUAL/FORECASTED thresholds tied to email alerts
- Least-privilege IAM role for Terraform deployment (GitHub Actions OIDC)

Update `variables.tf` values or pass overrides via `terraform.tfvars` for environment-specific settings (alert emails, budget limits, etc.).

## Deployment workflow

1. Ensure the remote state backend is provisioned (see **Remote state**).
2. Configure a GitHub environment secret set:
   - `AWS_DEV_TERRAFORM_ROLE_ARN` / `AWS_PROD_TERRAFORM_ROLE_ARN`
   - `GITHUB_OIDC_PROVIDER_ARN`
   - `TERRAFORM_ALERT_EMAIL_DEV` / `TERRAFORM_ALERT_EMAIL_PROD`
3. Configure GitHub environment protections for `dev-terraform-apply` and `prod-terraform-apply` to require manual approval before apply jobs run.
4. From your workstation, run:

   ```bash
   cd infrastructure/environments/dev
   terraform init
   terraform plan
   terraform apply
   ```

   Repeat for `prod` after changes are approved.

The GitHub Actions workflows (`terraform-dev.yml`, `terraform-prod.yml`) automatically run `terraform fmt`, `terraform validate`, and `terraform plan` on pushes and pull requests that modify infrastructure code. Manual `workflow_dispatch` runs with `action=apply` trigger gated applies that reuse the stored plan file.

## Secret management

The compute module provisions an AWS Secrets Manager secret per environment (toggle with `create_private_secret`) and optionally mirrors the secret ARN into AWS Systems Manager Parameter Store for discovery. IAM policies enforce that Terraform manages only resources tagged `ManagedBy = Terraform`, and Lambda functions receive the secret ARN via environment variables. Additional secrets (database credentials, API keys) should be stored in Secrets Manager (or Parameter Store when appropriate) and referenced at runtime via the provided IAM role.

## Cost management

- **Serverless-first design** keeps compute spend to a minimum (pay-per-request Lambda & HTTP API Gateway).
- **Budgets and alerts** raise ACTUAL and FORECASTED notifications to the configured email recipients.
- **Small CIDR ranges** limit VPC IP usage and avoid unnecessary NAT Gateways.
- **Flow logs optional**: disabled in dev, enabled in prod for security monitoring.

Regularly review AWS Cost Explorer and Budgets notifications to adjust limits and thresholds.

## Change management

- Open pull requests for every infrastructure change and require at least one peer review before merging.
- Update [`CHANGELOG.md`](./CHANGELOG.md) with user-facing notes for each release.
- Tag infrastructure releases (e.g., `infra-v1.0.0`) corresponding to merged pull requests.
- Use the GitHub Actions plan output and manual approval step as evidence during change review/approvals.

## Releases & logging

Maintain a cadence of release notes in `CHANGELOG.md` and link them in PR descriptions. Capture Terraform apply logs in the GitHub Actions run history (or export them to your change management system) for auditability.
