variable "name" {
  description = "Prefix for compute resources."
  type        = string
}

variable "environment" {
  description = "Environment name (dev, prod, etc.)."
  type        = string
}

variable "subnet_ids" {
  description = "List of subnet IDs for the Lambda function."
  type        = list(string)
}

variable "vpc_id" {
  description = "VPC ID for security group attachment."
  type        = string
}

variable "tags" {
  description = "Common tags applied to resources."
  type        = map(string)
  default     = {}
}

variable "create_private_secret" {
  description = "Toggle creation of a generated application secret."
  type        = bool
  default     = true
}

variable "create_ssm_parameter" {
  description = "Create an SSM parameter that references the application secret ARN."
  type        = bool
  default     = true
}

variable "alarm_email" {
  description = "Optional email for API invocation alarms."
  type        = string
  default     = null
}
