# ==============================================
# StarterKit Nginx Dockerfile
# ==============================================
# Production-optimized Nginx with custom configs

FROM nginx:1.25-alpine

LABEL maintainer="StarterKit Team"
LABEL description="Nginx reverse proxy for StarterKit API"

# Install additional tools
RUN apk add --no-cache \
    bash \
    curl \
    openssl \
    tzdata

# Create necessary directories
RUN mkdir -p /var/cache/nginx/api \
    /var/cache/nginx/static \
    /var/www/static \
    /var/www/certbot \
    /etc/nginx/ssl

# Copy custom nginx configuration
COPY nginx.conf /etc/nginx/nginx.conf
# Note: conf.d/ files are mounted via docker-compose volumes

# Generate self-signed certificate for development
RUN openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout /etc/nginx/ssl/key.pem \
    -out /etc/nginx/ssl/cert.pem \
    -subj "/C=US/ST=State/L=City/O=StarterKit/CN=localhost"

# Set proper permissions
RUN chown -R nginx:nginx /var/cache/nginx \
    && chown -R nginx:nginx /var/www \
    && chmod -R 755 /var/cache/nginx

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

# Expose ports
EXPOSE 80 443

# Run nginx
CMD ["nginx", "-g", "daemon off;"]
