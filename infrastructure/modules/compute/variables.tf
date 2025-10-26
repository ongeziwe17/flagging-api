variable "name" {
  description = "Service name prefix."
  type        = string
}

variable "environment" {
  description = "Environment name (dev, prod, etc.)."
  type        = string
}

variable "aws_region" {
  description = "AWS region for resources and logging."
  type        = string
}

variable "subnet_ids" {
  description = "List of subnet IDs for the ECS service."
  type        = list(string)
}

variable "vpc_id" {
  description = "VPC ID for security groups and load balancer."
  type        = string
}

variable "tags" {
  description = "Common tags applied to resources."
  type        = map(string)
  default     = {}
}

variable "container_image" {
  description = "Full container image URI to deploy."
  type        = string
}

variable "container_port" {
  description = "Port exposed by the container."
  type        = number
  default     = 8080
}

variable "desired_count" {
  description = "Desired number of ECS tasks."
  type        = number
  default     = 1
}

variable "task_cpu" {
  description = "CPU units for the task definition."
  type        = number
  default     = 512
}

variable "task_memory" {
  description = "Memory (MiB) for the task definition."
  type        = number
  default     = 1024
}

variable "environment_variables" {
  description = "Plain-text environment variables for the container."
  type        = map(string)
  default     = {}
}

variable "secrets" {
  description = "List of Secrets Manager ARNs mapped to container environment variables."
  type = list(object({
    name = string
    arn  = string
  }))
  default = []
}

variable "ssm_parameters" {
  description = "List of SSM parameter ARNs mapped to container environment variables."
  type = list(object({
    name = string
    arn  = string
  }))
  default = []
}

variable "create_task_role" {
  description = "Create a task IAM role for the application."
  type        = bool
  default     = false
}

variable "enable_execute_command" {
  description = "Enable ECS exec for interactive debugging."
  type        = bool
  default     = false
}

variable "assign_public_ip" {
  description = "Assign a public IP to Fargate tasks."
  type        = bool
  default     = true
}

variable "enable_container_insights" {
  description = "Enable CloudWatch Container Insights on the cluster."
  type        = bool
  default     = false
}

variable "log_retention_in_days" {
  description = "Retention period for ECS log group."
  type        = number
  default     = 30
}

variable "allowed_ingress_cidrs" {
  description = "CIDR blocks allowed to reach the load balancer."
  type        = list(string)
  default     = ["0.0.0.0/0"]
}

variable "load_balancer_port" {
  description = "Listener port for the public load balancer."
  type        = number
  default     = 80
}

variable "health_check_path" {
  description = "HTTP path for target group health checks."
  type        = string
  default     = "/health"
}
