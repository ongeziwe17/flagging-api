terraform {
  backend "s3" {
    bucket         = "feature-flagging-terraform-state"
    key            = "dev/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "feature-flagging-terraform-locks"
    encrypt        = true
  }
}
