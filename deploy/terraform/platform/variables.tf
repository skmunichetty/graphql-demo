variable "region" {
  type    = string
  default = "ap-southeast-2"
}

variable "cluster_name" {
  type    = string
  default = "graphql-demo"
}

variable "kubernetes_version" {
  type    = string
  default = "1.36"
}

variable "admin_cidrs" {
  type        = list(string)
  description = "Public IPv4 CIDRs allowed to reach the EKS API endpoint, e.g. your IP/32."
  validation {
    condition = length(var.admin_cidrs) > 0 && alltrue([
      for cidr in var.admin_cidrs : can(cidrnetmask(cidr)) && cidr != "0.0.0.0/0"
    ])
    error_message = "Provide restricted IPv4 CIDRs, such as your public IP/32."
  }
}

variable "node_instance_types" {
  type    = list(string)
  default = ["t3.medium"]
}

variable "node_desired_size" {
  type    = number
  default = 2
}

variable "github_repository" {
  type        = string
  description = "owner/name of the repository whose workflows may assume the CI and CD roles."
  default     = "skmunichetty/graphql-demo"
}

variable "ecr_repositories" {
  type    = list(string)
  default = ["estimates-api"]
}

variable "app_namespaces" {
  type        = list(string)
  description = "Namespaces the CD role may deploy to."
  default     = ["estimates"]
}
