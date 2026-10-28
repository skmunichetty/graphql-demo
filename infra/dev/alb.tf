# Public HTTPS entry point.
#   HTTPS :443 (TLS terminated here, ACM certificate) --> HTTP :8080 on the tasks
#   HTTP  :80  --> 301 redirect to HTTPS (done by the ALB; the app never sees these requests)
# The ALB adds X-Forwarded-Proto/For; the app trusts them (ASPNETCORE_FORWARDEDHEADERS_ENABLED),
# which is safe because only this ALB can reach the tasks (security group api_from_alb).

data "aws_route53_zone" "public" {
  name         = var.route53_zone_name
  private_zone = false
}

resource "aws_acm_certificate" "api" {
  domain_name       = var.domain_name
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_route53_record" "certificate_validation" {
  for_each = {
    for option in aws_acm_certificate.api.domain_validation_options : option.domain_name => {
      name   = option.resource_record_name
      type   = option.resource_record_type
      record = option.resource_record_value
    }
  }

  zone_id         = data.aws_route53_zone.public.zone_id
  name            = each.value.name
  type            = each.value.type
  records         = [each.value.record]
  ttl             = 300
  allow_overwrite = true
}

resource "aws_acm_certificate_validation" "api" {
  certificate_arn         = aws_acm_certificate.api.arn
  validation_record_fqdns = [for record in aws_route53_record.certificate_validation : record.fqdn]
}

resource "aws_lb" "api" {
  name               = local.name_prefix
  load_balancer_type = "application"
  internal           = false
  security_groups    = [aws_security_group.alb.id]
  subnets            = aws_subnet.public[*].id

  # Rejects requests with malformed headers instead of forwarding them.
  drop_invalid_header_fields = true

  # GraphQL subscriptions (SSE/WebSocket) keep connections open; allow 2 minutes of idle time.
  idle_timeout               = 120
  enable_deletion_protection = false
}

resource "aws_lb_target_group" "api" {
  name        = "${local.name_prefix}-api"
  vpc_id      = aws_vpc.main.id
  target_type = "ip" # awsvpc tasks are registered by their private IP
  protocol    = "HTTP"
  port        = 8080

  # Readiness: a task only receives traffic while it can reach the database. The app caps this
  # check at ~4 s, below the 5 s timeout, and never redirects /health/* to HTTPS.
  health_check {
    path                = "/health/ready"
    protocol            = "HTTP"
    matcher             = "200"
    interval            = 15
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }

  # Faster rollouts: in-flight requests get 30 s to finish before a draining task stops.
  deregistration_delay = 30
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.api.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-Res-PQ-2025-09" # TLS 1.3/1.2, restricted ciphers, post-quantum key exchange
  certificate_arn   = aws_acm_certificate_validation.api.certificate_arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }
}

resource "aws_lb_listener" "http_redirect" {
  load_balancer_arn = aws_lb.api.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "redirect"
    redirect {
      protocol    = "HTTPS"
      port        = "443"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_route53_record" "api" {
  zone_id = data.aws_route53_zone.public.zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_lb.api.dns_name
    zone_id                = aws_lb.api.zone_id
    evaluate_target_health = true
  }
}
