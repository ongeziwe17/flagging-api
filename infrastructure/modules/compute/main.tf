terraform {
  required_version = ">= 1.5.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
    archive = {
      source  = "hashicorp/archive"
      version = ">= 2.4"
    }
    random = {
      source  = "hashicorp/random"
      version = ">= 3.5"
    }
  }
}

data "archive_file" "lambda" {
  type        = "zip"
  source_dir  = "${path.module}/files"
  output_path = "${path.module}/files/app.zip"
}

resource "aws_cloudwatch_log_group" "lambda" {
  name              = "/aws/lambda/${var.name}-${var.environment}"
  retention_in_days = 14
  kms_key_id        = null

  tags = var.tags
}

resource "aws_iam_role" "lambda" {
  name = "${var.name}-${var.environment}-lambda-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = { Service = "lambda.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })

  tags = var.tags
}

resource "aws_iam_role_policy" "lambda_logging" {
  name = "${var.name}-${var.environment}-lambda-logging"
  role = aws_iam_role.lambda.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = [
        "logs:CreateLogStream",
        "logs:PutLogEvents"
      ]
      Resource = "${aws_cloudwatch_log_group.lambda.arn}:*"
    }]
  })
}

resource "aws_security_group" "lambda" {
  name        = "${var.name}-${var.environment}-lambda"
  description = "Allow HTTPS egress for Lambda"
  vpc_id      = var.vpc_id

  egress {
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "${var.name}-${var.environment}-lambda"
  })
}

resource "random_password" "app" {
  count  = var.create_private_secret ? 1 : 0
  length = 32
  special = true
}

resource "aws_secretsmanager_secret" "app" {
  count       = var.create_private_secret ? 1 : 0
  name        = "${var.name}/${var.environment}/app"
  description = "Application secret for ${var.environment} environment"
  kms_key_id  = null

  tags = var.tags
}

resource "aws_secretsmanager_secret_version" "app" {
  count     = var.create_private_secret ? 1 : 0
  secret_id = aws_secretsmanager_secret.app[0].id
  secret_string = jsonencode({
    signing_key = random_password.app[0].result
  })
}

resource "aws_lambda_function" "app" {
  function_name = "${var.name}-${var.environment}"
  description   = "Minimal API handler for ${var.environment}"
  role          = aws_iam_role.lambda.arn
  runtime       = "python3.11"
  handler       = "app.handler"
  filename      = data.archive_file.lambda.output_path
  source_code_hash = data.archive_file.lambda.output_base64sha256

  vpc_config {
    subnet_ids         = var.subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      POWERTOOLS_SERVICE_NAME = var.name
      ENVIRONMENT             = var.environment
      APP_SECRET_ARN          = var.create_private_secret ? aws_secretsmanager_secret.app[0].arn : ""
    }
  }

  tags = var.tags
}

resource "aws_apigatewayv2_api" "http" {
  name          = "${var.name}-${var.environment}"
  protocol_type = "HTTP"

  tags = var.tags
}

resource "aws_apigatewayv2_integration" "lambda" {
  api_id                 = aws_apigatewayv2_api.http.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.app.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "root" {
  api_id    = aws_apigatewayv2_api.http.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.lambda.id}"
}

resource "aws_apigatewayv2_stage" "default" {
  api_id      = aws_apigatewayv2_api.http.id
  name        = "$default"
  auto_deploy = true

  access_log_settings {
    destination_arn = aws_cloudwatch_log_group.api.arn
    format          = jsonencode({
      requestId      = "$context.requestId",
      ip             = "$context.identity.sourceIp",
      requestTime    = "$context.requestTime",
      routeKey       = "$context.routeKey",
      status         = "$context.status",
      integrationLat = "$context.integrationLatency"
    })
  }

  tags = var.tags
}

resource "aws_cloudwatch_log_group" "api" {
  name              = "API-Gateway-Execution-Logs_${aws_apigatewayv2_api.http.id}/$default"
  retention_in_days = 7
  kms_key_id        = null

  tags = var.tags
}

resource "aws_lambda_permission" "api_invoke" {
  statement_id  = "AllowExecutionFromAPIGateway"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.app.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.http.execution_arn}/*/*"
}

resource "aws_cloudwatch_metric_alarm" "lambda_errors" {
  alarm_name          = "${var.name}-${var.environment}-lambda-errors"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = aws_lambda_function.app.function_name
  }

  alarm_description = "Alert when Lambda errors occur"

  alarm_actions = var.alarm_email == null ? [] : [aws_sns_topic.alerts[0].arn]
  ok_actions    = var.alarm_email == null ? [] : [aws_sns_topic.alerts[0].arn]

  tags = var.tags
}

resource "aws_sns_topic" "alerts" {
  count = var.alarm_email == null ? 0 : 1
  name  = "${var.name}-${var.environment}-alerts"

  tags = var.tags
}

resource "aws_sns_topic_subscription" "email" {
  count     = var.alarm_email == null ? 0 : 1
  topic_arn = aws_sns_topic.alerts[0].arn
  protocol  = "email"
  endpoint  = var.alarm_email
}

resource "aws_ssm_parameter" "secret_pointer" {
  count = var.create_private_secret && var.create_ssm_parameter ? 1 : 0

  name  = "/${var.name}/${var.environment}/app-secret-arn"
  type  = "String"
  value = aws_secretsmanager_secret.app[0].arn
  tags  = var.tags
}

output "api_endpoint" {
  value = aws_apigatewayv2_api.http.api_endpoint
}

output "lambda_function_name" {
  value = aws_lambda_function.app.function_name
}

output "secret_arn" {
  value       = var.create_private_secret ? aws_secretsmanager_secret.app[0].arn : null
  description = "Secrets Manager ARN containing the generated application secret."
}

output "secret_parameter_name" {
  value       = var.create_private_secret && var.create_ssm_parameter ? aws_ssm_parameter.secret_pointer[0].name : null
  description = "SSM Parameter Store path containing the secret ARN pointer."
}
