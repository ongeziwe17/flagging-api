# Infrastructure Change Log

All notable infrastructure changes will be documented in this file.

## [0.1.0] - 2024-03-26
### Added
- Initial Terraform repository structure with networking and compute modules.
- Remote state backend using S3 + DynamoDB with documented access policy.
- Dev and prod environment configurations, AWS Budgets, and least-privilege IAM roles.
- GitHub Actions workflows for fmt/validate/plan and gated apply per environment.
- Cost management, deployment, and change management documentation.
