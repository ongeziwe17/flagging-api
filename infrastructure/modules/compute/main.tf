terraform {
  required_version = ">= 1.5.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

locals {
  resource_prefix = "${var.name}-${var.environment}"
}

resource "aws_ecs_cluster" "this" {
  name = "${local.resource_prefix}-cluster"

  setting {
    name  = "containerInsights"
    value = var.enable_container_insights ? "enabled" : "disabled"
  }

  tags = merge(var.tags, {
    Name = "${local.resource_prefix}-cluster"
  })
}

resource "aws_cloudwatch_log_group" "ecs" {
  name              = "/ecs/${local.resource_prefix}"
  retention_in_days = var.log_retention_in_days

  tags = var.tags
}

resource "aws_iam_role" "task_execution" {
  name = "${local.resource_prefix}-task-exec"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })

  tags = merge(var.tags, {
    Name = "${local.resource_prefix}-task-exec"
  })
}

resource "aws_iam_role_policy_attachment" "task_execution_managed" {
  role       = aws_iam_role.task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "task_execution_secrets" {
  count = (length(var.secrets) + length(var.ssm_parameters)) > 0 ? 1 : 0

  name = "${local.resource_prefix}-task-exec-secrets"
  role = aws_iam_role.task_execution.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = [
        "secretsmanager:GetSecretValue",
        "ssm:GetParameters"
      ]
      Resource = concat(
        [for secret in var.secrets : secret.arn],
        [for param in var.ssm_parameters : param.arn]
      )
    }]
  })
}

resource "aws_iam_role" "task" {
  count = var.create_task_role ? 1 : 0

  name = "${local.resource_prefix}-task"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })

  tags = merge(var.tags, {
    Name = "${local.resource_prefix}-task"
  })
}

resource "aws_security_group" "service" {
  name        = "${local.resource_prefix}-svc"
  description = "Allow inbound traffic from load balancer"
  vpc_id      = var.vpc_id

  ingress {
    description      = "Allow traffic from ALB"
    from_port        = var.container_port
    to_port          = var.container_port
    protocol         = "tcp"
    security_groups  = [aws_security_group.lb.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "${local.resource_prefix}-svc"
  })
}

resource "aws_security_group" "lb" {
  name        = "${local.resource_prefix}-alb"
  description = "Allow inbound HTTP"
  vpc_id      = var.vpc_id

  ingress {
    description = "Inbound HTTP"
    from_port   = var.load_balancer_port
    to_port     = var.load_balancer_port
    protocol    = "tcp"
    cidr_blocks = var.allowed_ingress_cidrs
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "${local.resource_prefix}-alb"
  })
}

resource "aws_lb" "this" {
  name               = substr(replace("${local.resource_prefix}-alb", "_", "-"), 0, 32)
  internal           = false
  load_balancer_type = "application"
  security_groups    = [aws_security_group.lb.id]
  subnets            = var.subnet_ids

  tags = merge(var.tags, {
    Name = "${local.resource_prefix}-alb"
  })
}

resource "aws_lb_target_group" "this" {
  name        = substr(replace("${local.resource_prefix}-tg", "_", "-"), 0, 32)
  port        = var.container_port
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = var.vpc_id

  health_check {
    enabled             = true
    interval            = 30
    matcher             = "200-399"
    path                = var.health_check_path
    healthy_threshold   = 3
    unhealthy_threshold = 2
    timeout             = 5
  }

  tags = merge(var.tags, {
    Name = "${local.resource_prefix}-tg"
  })
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.this.arn
  port              = var.load_balancer_port
  protocol          = "HTTP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.this.arn
  }
}

resource "aws_ecs_task_definition" "this" {
  family                   = "${local.resource_prefix}"
  cpu                      = var.task_cpu
  memory                   = var.task_memory
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  execution_role_arn       = aws_iam_role.task_execution.arn
  task_role_arn            = var.create_task_role ? aws_iam_role.task[0].arn : null

  container_definitions = jsonencode([
    {
      name      = "${var.name}"
      image     = var.container_image
      essential = true
      portMappings = [
        {
          containerPort = var.container_port
          hostPort      = var.container_port
          protocol      = "tcp"
        }
      ]
      environment = [for key, value in var.environment_variables : {
        name  = key
        value = value
      }]
      secrets = concat(
        [for secret in var.secrets : {
          name      = secret.name
          valueFrom = secret.arn
        }],
        [for param in var.ssm_parameters : {
          name      = param.name
          valueFrom = param.arn
        }]
      )
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.ecs.name
          awslogs-region        = var.aws_region
          awslogs-stream-prefix = "ecs"
        }
      }
    }
  ])

  tags = var.tags
}

resource "aws_ecs_service" "this" {
  name            = "${local.resource_prefix}-svc"
  cluster         = aws_ecs_cluster.this.id
  desired_count   = var.desired_count
  launch_type     = "FARGATE"
  task_definition = aws_ecs_task_definition.this.arn
  enable_execute_command = var.enable_execute_command

  network_configuration {
    assign_public_ip = var.assign_public_ip
    security_groups  = [aws_security_group.service.id]
    subnets          = var.subnet_ids
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.this.arn
    container_name   = var.name
    container_port   = var.container_port
  }

  lifecycle {
    ignore_changes = [desired_count]
  }

  tags = var.tags

  depends_on = [aws_lb_listener.http]
}

output "service_arn" {
  value = aws_ecs_service.this.arn
}

output "cluster_name" {
  value = aws_ecs_cluster.this.name
}

output "load_balancer_dns_name" {
  value = aws_lb.this.dns_name
}

output "task_execution_role_arn" {
  value = aws_iam_role.task_execution.arn
}

output "task_role_arn" {
  value       = var.create_task_role ? aws_iam_role.task[0].arn : null
  description = "ARN of the task role if created"
}
