variable "aws_region" {
  description = "AWS region to deploy the prod environment."
  type        = string
  default     = "us-east-1"
}

variable "name_prefix" {
  description = "Resource prefix for prod environment."
  type        = string
  default     = "feature-flagging"
}

variable "budget_limit" {
  description = "Monthly cost threshold for prod environment."
  type        = number
  default     = 200
}

variable "alarm_email" {
  description = "Email address for receiving alerts."
  type        = string
  default     = null
}

variable "github_actions_repository" {
  description = "GitHub repository (owner/name) allowed to assume the Terraform role."
  type        = string
  default     = "Feature-Flagging-Org/flagging-api"
}

variable "github_oidc_provider_arn" {
  description = "ARN of the AWS IAM OIDC provider for GitHub Actions."
  type        = string
  default     = "arn:aws:iam::123456789012:oidc-provider/token.actions.githubusercontent.com"
}
