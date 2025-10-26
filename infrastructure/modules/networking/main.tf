terraform {
  required_version = ">= 1.5.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

resource "aws_vpc" "this" {
  cidr_block           = var.cidr_block
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = merge({
    Name        = "${var.name}-vpc"
    Environment = var.tags["Environment"]
  }, var.tags)
}

resource "aws_internet_gateway" "this" {
  vpc_id = aws_vpc.this.id

  tags = merge({
    Name = "${var.name}-igw"
  }, var.tags)
}

resource "aws_subnet" "public" {
  for_each = { for idx, cidr in var.public_subnet_cidrs : idx => cidr }

  vpc_id                  = aws_vpc.this.id
  cidr_block              = each.value
  map_public_ip_on_launch = true

  tags = merge({
    Name        = "${var.name}-public-${each.key}"
    Tier        = "public"
    Environment = var.tags["Environment"]
  }, var.tags)
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.this.id

  tags = merge({
    Name = "${var.name}-public"
  }, var.tags)
}

resource "aws_route" "public_internet" {
  route_table_id         = aws_route_table.public.id
  destination_cidr_block = "0.0.0.0/0"
  gateway_id             = aws_internet_gateway.this.id
}

resource "aws_route_table_association" "public" {
  for_each = aws_subnet.public

  subnet_id      = each.value.id
  route_table_id = aws_route_table.public.id
}

resource "aws_cloudwatch_log_group" "flow_logs" {
  count = var.enable_flow_logs ? 1 : 0

  name              = "/aws/vpc/${var.name}-flow-logs"
  retention_in_days = var.flow_log_retention_days
  kms_key_id        = null

  tags = var.tags
}

resource "aws_flow_log" "vpc" {
  count = var.enable_flow_logs ? 1 : 0

  log_destination_type = "cloud-watch-logs"
  log_group_name       = aws_cloudwatch_log_group.flow_logs[0].name
  traffic_type         = "ALL"
  vpc_id               = aws_vpc.this.id
  iam_role_arn         = null
}

output "vpc_id" {
  value = aws_vpc.this.id
}

output "public_subnet_ids" {
  value = [for subnet in aws_subnet.public : subnet.id]
}
