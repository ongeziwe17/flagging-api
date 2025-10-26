variable "aws_region" {
  description = "AWS region to deploy the dev environment."
  type        = string
  default     = "us-east-1"
}

variable "name_prefix" {
  description = "Resource prefix for dev environment."
  type        = string
  default     = "feature-flagging"
}

variable "budget_limit" {
  description = "Monthly cost threshold for dev environment."
  type        = number
  default     = 20
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

variable "container_image" {
  description = "Container image URI for the API."
  type        = string
}

variable "container_port" {
  description = "Port exposed by the API container."
  type        = number
  default     = 8080
}

variable "task_cpu" {
  description = "Fargate task CPU units."
  type        = number
  default     = 512
}

variable "task_memory" {
  description = "Fargate task memory (MiB)."
  type        = number
  default     = 1024
}

variable "desired_count" {
  description = "Number of tasks to run in dev."
  type        = number
  default     = 1
}

variable "environment_variables" {
  description = "Plain-text environment variables for the API."
  type        = map(string)
  default     = {}
}

variable "environment_secrets" {
  description = "Map of environment variable names to Secrets Manager ARNs."
  type        = map(string)
  default     = {}
}

variable "environment_parameters" {
  description = "Map of environment variable names to SSM parameter ARNs."
  type        = map(string)
  default     = {}
}
