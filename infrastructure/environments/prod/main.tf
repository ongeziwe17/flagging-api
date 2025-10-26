terraform {
  required_version = ">= 1.5.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

data "aws_caller_identity" "current" {}

locals {
  environment = "prod"
  common_tags = {
    Environment = local.environment
    Project     = "feature-flagging-api"
    ManagedBy   = "Terraform"
  }
}

module "networking" {
  source              = "../../modules/networking"
  name                = "${var.name_prefix}-${local.environment}"
  cidr_block          = "10.1.0.0/24"
  public_subnet_cidrs = ["10.1.1.0/26", "10.1.1.64/26"]
  enable_flow_logs    = true
  tags                = merge(local.common_tags, { Component = "networking" })
}

module "compute" {
  source      = "../../modules/compute"
  name        = var.name_prefix
  environment = local.environment
  subnet_ids  = module.networking.public_subnet_ids
  vpc_id      = module.networking.vpc_id
  tags        = merge(local.common_tags, { Component = "compute" })
  alarm_email = var.alarm_email
}

resource "aws_budgets_budget" "monthly" {
  name              = "${local.environment}-monthly-budget"
  budget_type       = "COST"
  limit_amount      = var.budget_limit
  limit_unit        = "USD"
  time_period_start = "2024-01-01_00:00"
  time_unit         = "MONTHLY"

  cost_filters = {
    TagKeyValue = "Environment$${local.environment}"
  }

  notification {
    comparison_operator        = "GREATER_THAN"
    notification_type          = "ACTUAL"
    threshold                  = 75
    threshold_type             = "PERCENTAGE"
    subscriber_email_addresses = var.alarm_email == null ? [] : [var.alarm_email]
  }

  notification {
    comparison_operator        = "GREATER_THAN"
    notification_type          = "FORECASTED"
    threshold                  = 90
    threshold_type             = "PERCENTAGE"
    subscriber_email_addresses = var.alarm_email == null ? [] : [var.alarm_email]
  }
}
