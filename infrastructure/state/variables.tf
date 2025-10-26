variable "aws_region" {
  description = "AWS region for Terraform state resources."
  type        = string
}

variable "bucket_name" {
  description = "Name of the S3 bucket for remote state."
  type        = string
}

variable "dynamodb_table_name" {
  description = "Name of the DynamoDB table used for state locking."
  type        = string
}

variable "kms_master_key_id" {
  description = "Optional KMS key ARN for bucket encryption."
  type        = string
  default     = null
}

variable "allowed_role_arns" {
  description = "List of IAM role ARNs permitted to access the state bucket."
  type        = list(string)
  default     = []
}

variable "tags" {
  description = "Common tags for state resources."
  type        = map(string)
  default     = {}
}
