variable "name" {
  description = "Prefix to use for networking resources."
  type        = string
}

variable "cidr_block" {
  description = "CIDR block for the VPC."
  type        = string
}

variable "public_subnet_cidrs" {
  description = "List of CIDR blocks for public subnets."
  type        = list(string)
}

variable "enable_flow_logs" {
  description = "Enable VPC flow logs (additional cost)."
  type        = bool
  default     = false
}

variable "flow_log_retention_days" {
  description = "CloudWatch log retention for flow logs."
  type        = number
  default     = 14
}

variable "tags" {
  description = "Additional tags to apply."
  type        = map(string)
  default     = {}
}
