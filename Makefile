# ==============================================
# StarterKit Makefile
# ==============================================
# Professional commands for Docker management

.PHONY: help build up down restart logs ps clean prune dev prod tools backup restore nginx-dev nginx-prod nginx-logs nginx-test nginx-reload docs-check arch-test

# Default target
.DEFAULT_GOAL := help

# Project settings (override on the command line, e.g. `make db-shell DB_USER=me`)
APP_NAME ?= starterkit
APP_DIR  ?= /opt/$(APP_NAME)
DB_USER  ?= $(APP_NAME)_user
DB_NAME  ?= $(APP_NAME)

# Colors for output
BLUE := \033[0;34m
GREEN := \033[0;32m
YELLOW := \033[1;33m
RED := \033[0;31m
NC := \033[0m # No Color

# ==============================================
# Help
# ==============================================
help: ## Show this help message
	@echo "$(BLUE)═══════════════════════════════════════════════$(NC)"
	@echo "$(GREEN)  StarterKit Docker Commands$(NC)"
	@echo "$(BLUE)═══════════════════════════════════════════════$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "$(YELLOW)%-20s$(NC) %s\n", $$1, $$2}'
	@echo ""

# ==============================================
# Development Commands
# ==============================================
build: ## Build all Docker images
	@echo "$(GREEN)Building Docker images...$(NC)"
	docker-compose build --no-cache

up: ## Start all services in development mode
	@echo "$(GREEN)Starting services in development mode...$(NC)"
	docker-compose up -d

dev: ## Start all services with development tools (PgAdmin, Redis Commander)
	@echo "$(GREEN)Starting services with development tools...$(NC)"
	docker-compose --profile dev up -d

down: ## Stop all services
	@echo "$(YELLOW)Stopping all services...$(NC)"
	docker-compose down

restart: ## Restart all services
	@echo "$(YELLOW)Restarting all services...$(NC)"
	docker-compose restart

stop: ## Stop all services without removing containers
	@echo "$(YELLOW)Stopping services...$(NC)"
	docker-compose stop

# ==============================================
# Production Commands
# ==============================================
prod: ## Start services in production mode
	@echo "$(GREEN)Starting services in production mode...$(NC)"
	docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d

prod-build: ## Build and start in production mode
	@echo "$(GREEN)Building and starting in production mode...$(NC)"
	docker-compose -f docker-compose.yml -f docker-compose.prod.yml build --no-cache
	docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d

prod-down: ## Stop production services
	@echo "$(YELLOW)Stopping production services...$(NC)"
	docker-compose -f docker-compose.yml -f docker-compose.prod.yml down

# ==============================================
# Monitoring Commands
# ==============================================
logs: ## Show logs from all services
	@echo "$(BLUE)Showing logs...$(NC)"
	docker-compose logs -f

logs-api: ## Show API logs
	@echo "$(BLUE)Showing API logs...$(NC)"
	docker-compose logs -f api

logs-db: ## Show PostgreSQL logs
	@echo "$(BLUE)Showing PostgreSQL logs...$(NC)"
	docker-compose logs -f postgres

logs-redis: ## Show Redis logs
	@echo "$(BLUE)Showing Redis logs...$(NC)"
	docker-compose logs -f redis

logs-seq: ## Show Seq logs
	@echo "$(BLUE)Showing Seq logs...$(NC)"
	docker-compose logs -f seq

ps: ## List running containers
	@echo "$(BLUE)Running containers:$(NC)"
	docker-compose ps

status: ## Show detailed status of services
	@echo "$(BLUE)Service status:$(NC)"
	@docker-compose ps --format "table {{.Name}}\t{{.Status}}\t{{.Ports}}"

# ==============================================
# Database Commands
# ==============================================
db-migrate: ## Run database migrations
	@echo "$(GREEN)Running database migrations...$(NC)"
	docker-compose exec api dotnet ef database update

db-seed: ## Seed database with initial data
	@echo "$(GREEN)Seeding database...$(NC)"
	docker-compose exec api dotnet run --seed

db-reset: ## Reset database (WARNING: Destroys all data!)
	@echo "$(RED)Resetting database... This will delete all data!$(NC)"
	@read -p "Are you sure? [y/N] " -n 1 -r; \
	if [[ $$REPLY =~ ^[Yy]$$ ]]; then \
		docker-compose exec api dotnet ef database drop -f; \
		docker-compose exec api dotnet ef database update; \
	fi

db-backup: ## Backup PostgreSQL database
	@echo "$(GREEN)Backing up database...$(NC)"
	@mkdir -p ./backups
	docker-compose exec -T postgres pg_dump -U $(DB_USER) -d $(DB_NAME) > ./backups/backup_$$(date +%Y%m%d_%H%M%S).sql
	@echo "$(GREEN)Backup completed: ./backups/backup_$$(date +%Y%m%d_%H%M%S).sql$(NC)"

db-restore: ## Restore PostgreSQL database from backup
	@echo "$(YELLOW)Restoring database from backup...$(NC)"
	@ls -1 ./backups/*.sql 2>/dev/null || (echo "$(RED)No backups found!$(NC)" && exit 1)
	@echo "Available backups:"
	@ls -1 ./backups/*.sql
	@read -p "Enter backup filename: " backup; \
	docker-compose exec -T postgres psql -U $(DB_USER) -d $(DB_NAME) < ./backups/$$backup
	@echo "$(GREEN)Database restored!$(NC)"

# ==============================================
# Cache Commands
# ==============================================
cache-clear: ## Clear Redis cache
	@echo "$(YELLOW)Clearing Redis cache...$(NC)"
	docker-compose exec redis redis-cli -a ${REDIS_PASSWORD} FLUSHALL
	@echo "$(GREEN)Cache cleared!$(NC)"

cache-stats: ## Show Redis cache statistics
	@echo "$(BLUE)Redis cache statistics:$(NC)"
	docker-compose exec redis redis-cli -a ${REDIS_PASSWORD} INFO stats

# ==============================================
# Maintenance Commands
# ==============================================
clean: ## Remove stopped containers and volumes
	@echo "$(YELLOW)Cleaning up...$(NC)"
	docker-compose down -v
	@echo "$(GREEN)Cleanup completed!$(NC)"

prune: ## Remove all unused Docker resources (WARNING: Aggressive cleanup!)
	@echo "$(RED)Pruning Docker resources...$(NC)"
	@read -p "This will remove ALL unused containers, networks, images. Continue? [y/N] " -n 1 -r; \
	if [[ $$REPLY =~ ^[Yy]$$ ]]; then \
		docker system prune -af --volumes; \
	fi

volumes: ## List all volumes
	@echo "$(BLUE)Docker volumes:$(NC)"
	docker volume ls | grep $(APP_NAME)

volumes-clean: ## Remove all volumes (WARNING: Destroys all data!)
	@echo "$(RED)Removing all volumes... This will delete all data!$(NC)"
	@read -p "Are you sure? [y/N] " -n 1 -r; \
	if [[ $$REPLY =~ ^[Yy]$$ ]]; then \
		docker-compose down -v; \
	fi

# ==============================================
# Tools Commands
# ==============================================
tools: ## Start development tools (PgAdmin, Redis Commander)
	@echo "$(GREEN)Starting development tools...$(NC)"
	docker-compose --profile tools up -d pgadmin redis-commander

tools-down: ## Stop development tools
	@echo "$(YELLOW)Stopping development tools...$(NC)"
	docker-compose stop pgadmin redis-commander

# ==============================================
# Health Check Commands
# ==============================================
health: ## Check health of all services
	@echo "$(BLUE)Checking service health...$(NC)"
	@docker-compose ps --format "table {{.Name}}\t{{.Status}}"
	@echo ""
	@echo "$(BLUE)API Health:$(NC)"
	@curl -f http://localhost:8080/health 2>/dev/null && echo "$(GREEN)✓ API is healthy$(NC)" || echo "$(RED)✗ API is unhealthy$(NC)"
	@echo "$(BLUE)PostgreSQL Health:$(NC)"
	@docker-compose exec -T postgres pg_isready -U $(DB_USER) && echo "$(GREEN)✓ PostgreSQL is healthy$(NC)" || echo "$(RED)✗ PostgreSQL is unhealthy$(NC)"
	@echo "$(BLUE)Redis Health:$(NC)"
	@docker-compose exec -T redis redis-cli -a ${REDIS_PASSWORD} ping 2>/dev/null && echo "$(GREEN)✓ Redis is healthy$(NC)" || echo "$(RED)✗ Redis is unhealthy$(NC)"

# ==============================================
# Access Commands
# ==============================================
shell-api: ## Open shell in API container
	@echo "$(BLUE)Opening API shell...$(NC)"
	docker-compose exec api /bin/sh

shell-db: ## Open PostgreSQL shell
	@echo "$(BLUE)Opening PostgreSQL shell...$(NC)"
	docker-compose exec postgres psql -U $(DB_USER) -d $(DB_NAME)

shell-redis: ## Open Redis CLI
	@echo "$(BLUE)Opening Redis CLI...$(NC)"
	docker-compose exec redis redis-cli -a ${REDIS_PASSWORD}

shell-nginx: ## Open Nginx shell
	@echo "$(BLUE)Opening Nginx shell...$(NC)"
	docker-compose exec nginx /bin/sh

# ==============================================
# Nginx Commands
# ==============================================
nginx-dev: ## Start Nginx with development config
	@echo "$(GREEN)Starting Nginx with development config...$(NC)"
	docker-compose --profile nginx up -d nginx

nginx-prod: ## Start Nginx with production config
	@echo "$(GREEN)Starting Nginx with production config...$(NC)"
	NGINX_CONFIG_FILE=app.conf docker-compose up -d nginx

nginx-logs: ## View Nginx logs (real-time)
	@echo "$(BLUE)Viewing Nginx logs...$(NC)"
	docker-compose logs -f nginx

nginx-test: ## Test Nginx configuration
	@echo "$(BLUE)Testing Nginx configuration...$(NC)"
	docker-compose exec nginx nginx -t

nginx-reload: ## Reload Nginx configuration (zero downtime)
	@echo "$(GREEN)Reloading Nginx configuration...$(NC)"
	docker-compose exec nginx nginx -s reload

nginx-status: ## Show Nginx status
	@echo "$(BLUE)Nginx Status:$(NC)"
	@docker-compose ps nginx
	@echo ""
	@echo "$(BLUE)Testing health endpoint:$(NC)"
	@curl -s http://localhost/health || echo "$(RED)Failed to connect$(NC)"

nginx-build: ## Build Nginx image
	@echo "$(GREEN)Building Nginx image...$(NC)"
	docker-compose build nginx

# ==============================================
# VPS Deployment Commands
# ==============================================
deploy-vps: ## Deploy to VPS production server
	@echo "$(GREEN)Deploying to VPS...$(NC)"
	@if [ -z "$$VPS_IP" ]; then \
		echo "$(RED)Error: VPS_IP environment variable not set$(NC)"; \
		echo "$(YELLOW)Usage: VPS_IP=your.server.ip make deploy-vps$(NC)"; \
		exit 1; \
	fi
	@echo "$(BLUE)Target: $$VPS_IP$(NC)"
	@echo "$(YELLOW)Step 1: Syncing files to VPS...$(NC)"
	rsync -avz --exclude 'bin/' --exclude 'obj/' --exclude '.git/' \
		--exclude 'postgres/' --exclude 'redis/' \
		-e "ssh -o StrictHostKeyChecking=no" \
		./ ubuntu@$$VPS_IP:$(APP_DIR)/
	@echo "$(YELLOW)Step 2: Running deployment script on VPS...$(NC)"
	ssh ubuntu@$$VPS_IP "cd $(APP_DIR) && chmod +x deployment/deploy.sh && ./deployment/deploy.sh"
	@echo "$(GREEN)✓ Deployment completed!$(NC)"

deploy-vps-with-key: ## Deploy to VPS with SSH key
	@echo "$(GREEN)Deploying to VPS with SSH key...$(NC)"
	@if [ -z "$$VPS_IP" ] || [ -z "$$SSH_KEY" ]; then \
		echo "$(RED)Error: VPS_IP and SSH_KEY required$(NC)"; \
		echo "$(YELLOW)Usage: VPS_IP=x.x.x.x SSH_KEY=~/.ssh/key.pem make deploy-vps-with-key$(NC)"; \
		exit 1; \
	fi
	@echo "$(BLUE)Target: $$VPS_IP$(NC)"
	@echo "$(YELLOW)Syncing files...$(NC)"
	rsync -avz --exclude 'bin/' --exclude 'obj/' --exclude '.git/' \
		--exclude 'postgres/' --exclude 'redis/' \
		-e "ssh -i $$SSH_KEY -o StrictHostKeyChecking=no" \
		./ ubuntu@$$VPS_IP:$(APP_DIR)/
	@echo "$(YELLOW)Running deployment...$(NC)"
	ssh -i $$SSH_KEY ubuntu@$$VPS_IP "cd $(APP_DIR) && chmod +x deployment/deploy.sh && ./deployment/deploy.sh"
	@echo "$(GREEN)✓ Deployment completed!$(NC)"

vps-setup: ## Initial VPS setup (run once)
	@echo "$(GREEN)Setting up VPS...$(NC)"
	@if [ -z "$$VPS_IP" ]; then \
		echo "$(RED)Error: VPS_IP required$(NC)"; \
		exit 1; \
	fi
	@echo "$(YELLOW)Creating directories...$(NC)"
	ssh ubuntu@$$VPS_IP "sudo mkdir -p $(APP_DIR) /backup && sudo chown $$USER:$$USER $(APP_DIR) /backup"
	@echo "$(YELLOW)Installing Docker...$(NC)"
	ssh ubuntu@$$VPS_IP "curl -fsSL https://get.docker.com -o get-docker.sh && sudo sh get-docker.sh && sudo usermod -aG docker $$USER"
	@echo "$(YELLOW)Installing Nginx...$(NC)"
	ssh ubuntu@$$VPS_IP "sudo apt update && sudo apt install -y nginx"
	@echo "$(GREEN)✓ VPS setup completed! Re-login to apply docker group$(NC)"

vps-logs: ## View logs from VPS
	@if [ -z "$$VPS_IP" ]; then echo "$(RED)VPS_IP required$(NC)"; exit 1; fi
	ssh ubuntu@$$VPS_IP "cd $(APP_DIR) && docker compose logs -f"

vps-status: ## Check VPS services status
	@if [ -z "$$VPS_IP" ]; then echo "$(RED)VPS_IP required$(NC)"; exit 1; fi
	@echo "$(BLUE)Checking VPS services...$(NC)"
	ssh ubuntu@$$VPS_IP "cd $(APP_DIR) && docker compose ps && echo '' && ./deployment/scripts/health-check.sh"

vps-backup: ## Backup VPS database
	@if [ -z "$$VPS_IP" ]; then echo "$(RED)VPS_IP required$(NC)"; exit 1; fi
	@echo "$(YELLOW)Creating backup on VPS...$(NC)"
	ssh ubuntu@$$VPS_IP "cd $(APP_DIR) && ./deployment/scripts/backup-db.sh manual"
	@echo "$(GREEN)✓ Backup completed$(NC)"

vps-restart: ## Restart services on VPS
	@if [ -z "$$VPS_IP" ]; then echo "$(RED)VPS_IP required$(NC)"; exit 1; fi
	@echo "$(YELLOW)Restarting services...$(NC)"
	ssh ubuntu@$$VPS_IP "cd $(APP_DIR) && docker compose restart"
	@echo "$(GREEN)✓ Services restarted$(NC)"

vps-shell: ## Open shell on VPS
	@if [ -z "$$VPS_IP" ]; then echo "$(RED)VPS_IP required$(NC)"; exit 1; fi
	ssh ubuntu@$$VPS_IP

vps-nginx-setup: ## Setup Nginx on VPS
	@if [ -z "$$VPS_IP" ]; then echo "$(RED)VPS_IP required$(NC)"; exit 1; fi
	@echo "$(YELLOW)Setting up Nginx on VPS...$(NC)"
	ssh ubuntu@$$VPS_IP "cd $(APP_DIR) && \
		sudo cp deployment/nginx/nginx.conf /etc/nginx/nginx.conf && \
		sudo cp deployment/nginx/conf.d/app-production.conf /etc/nginx/sites-available/$(APP_NAME) && \
		sudo ln -sf /etc/nginx/sites-available/$(APP_NAME) /etc/nginx/sites-enabled/ && \
		sudo rm -f /etc/nginx/sites-enabled/default && \
		sudo nginx -t && sudo systemctl restart nginx"
	@echo "$(GREEN)✓ Nginx configured$(NC)"

vps-ssl: ## Setup SSL certificate on VPS
	@if [ -z "$$VPS_IP" ] || [ -z "$$DOMAIN" ]; then \
		echo "$(RED)VPS_IP and DOMAIN required$(NC)"; \
		echo "$(YELLOW)Usage: VPS_IP=x.x.x.x DOMAIN=example.com make vps-ssl$(NC)"; \
		exit 1; \
	fi
	@echo "$(YELLOW)Installing SSL certificate for $$DOMAIN...$(NC)"
	ssh ubuntu@$$VPS_IP "sudo apt install -y certbot python3-certbot-nginx && \
		sudo certbot --nginx -d $$DOMAIN --non-interactive --agree-tos -m admin@$$DOMAIN"
	@echo "$(GREEN)✓ SSL certificate installed$(NC)"

# ==============================================
# URL Access
# ==============================================
urls: ## Show all service URLs
	@echo "$(BLUE)═══════════════════════════════════════════════$(NC)"
	@echo "$(GREEN)  Service URLs$(NC)"
	@echo "$(BLUE)═══════════════════════════════════════════════$(NC)"
	@echo "$(YELLOW)Nginx (HTTP):$(NC)     http://localhost"
	@echo "$(YELLOW)Nginx (HTTPS):$(NC)    https://localhost"
	@echo "$(YELLOW)API (Direct):$(NC)     http://localhost:8080"
	@echo "$(YELLOW)Swagger:$(NC)          http://localhost/swagger"
	@echo "$(YELLOW)Seq Logs:$(NC)         http://localhost:5341"
	@echo "$(YELLOW)PgAdmin:$(NC)          http://localhost:5050"
	@echo "$(YELLOW)Redis Commander:$(NC)  http://localhost:8081"
	@echo "$(BLUE)═══════════════════════════════════════════════$(NC)"

# ==============================================
# Quality
# ==============================================

docs-check: ## Validate documentation links, INDEX coverage and placeholders
	@bash scripts/check-docs.sh

arch-test: ## Run the architecture guardrail tests (no Docker needed)
	@dotnet test src/Api.Tests
