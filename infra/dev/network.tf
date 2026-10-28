# A dedicated VPC with two tiers in two Availability Zones:
#
#   public subnets  10.40.0.0/24, 10.40.1.0/24   ALB + Fargate tasks; route to the internet gateway
#   database subnets 10.40.10.0/24, 10.40.11.0/24 RDS only; NO route to the internet
#
# LEARNING COST TRADE-OFF: tasks run in public subnets with a public IP so they can reach ECR,
# CloudWatch Logs, Secrets Manager and the OIDC issuer without a NAT gateway (~US$45/month per AZ).
# Security groups still block every inbound connection except the ALB on port 8080.
# Production would use private task subnets with NAT or VPC endpoints.

locals {
  name_prefix = "${var.project}-${var.environment}" # graphql-demo-dev
  azs         = slice(data.aws_availability_zones.available.names, 0, 2)
}

data "aws_availability_zones" "available" {
  state = "available"
}

resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags                 = { Name = local.name_prefix }
}

resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = local.name_prefix }
}

resource "aws_subnet" "public" {
  count             = 2
  vpc_id            = aws_vpc.main.id
  availability_zone = local.azs[count.index]
  cidr_block        = cidrsubnet(var.vpc_cidr, 8, count.index) # 10.40.0.0/24, 10.40.1.0/24

  # Public IPs are requested per task (assign_public_ip in the ECS service), not for everything.
  map_public_ip_on_launch = false

  tags = { Name = "${local.name_prefix}-public-${local.azs[count.index]}" }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = "${local.name_prefix}-public" }
}

resource "aws_route" "public_internet" {
  route_table_id         = aws_route_table.public.id
  destination_cidr_block = "0.0.0.0/0"
  gateway_id             = aws_internet_gateway.main.id
}

resource "aws_route_table_association" "public" {
  count          = 2
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

resource "aws_subnet" "database" {
  count             = 2
  vpc_id            = aws_vpc.main.id
  availability_zone = local.azs[count.index]
  cidr_block        = cidrsubnet(var.vpc_cidr, 8, 10 + count.index) # 10.40.10.0/24, 10.40.11.0/24
  tags              = { Name = "${local.name_prefix}-database-${local.azs[count.index]}" }
}

# Only the implicit "local" route: the database subnets cannot reach or be reached from the internet.
resource "aws_route_table" "database" {
  vpc_id = aws_vpc.main.id
  tags   = { Name = "${local.name_prefix}-database" }
}

resource "aws_route_table_association" "database" {
  count          = 2
  subnet_id      = aws_subnet.database[count.index].id
  route_table_id = aws_route_table.database.id
}
