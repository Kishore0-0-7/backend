#!/bin/bash

# TurfManagement ASP.NET Core Deployment Script
# Created: June 24, 2025

# Exit on error
set -e

echo "=== TurfManagement Deployment Script ==="

# Check for dotnet
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK is not installed."
    echo "Please install .NET SDK 8.0 or later from https://dotnet.microsoft.com/download"
    exit 1
fi

# Define variables
DEPLOY_DIR="/opt/turfmanagement"
SERVICE_USER="$(whoami)"
PORT=5125

# Create deployment directory if it doesn't exist
echo "Creating deployment directory..."
sudo mkdir -p $DEPLOY_DIR
sudo chown $SERVICE_USER:$SERVICE_USER $DEPLOY_DIR

# Extract application files
echo "Extracting application files..."
tar -xzf turfmanagement-deploy.tar.gz -C $DEPLOY_DIR

# Create systemd service file for auto-start on boot
echo "Setting up systemd service..."
cat << EOF | sudo tee /etc/systemd/system/turfmanagement.service
[Unit]
Description=TurfManagement ASP.NET Core Application
After=network.target

[Service]
WorkingDirectory=$DEPLOY_DIR
ExecStart=/usr/bin/dotnet $DEPLOY_DIR/turfmanagement.dll --urls=http://0.0.0.0:$PORT
Restart=always
RestartSec=10
SyslogIdentifier=turfmanagement
User=$SERVICE_USER
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
EOF

# Enable and start the service
echo "Enabling and starting the service..."
sudo systemctl enable turfmanagement.service
sudo systemctl start turfmanagement.service

echo ""
echo "=== Deployment Complete ==="
echo "The TurfManagement application has been deployed to $DEPLOY_DIR"
echo "It is running on port $PORT and configured to start automatically on system boot."
echo "You can check the status with: sudo systemctl status turfmanagement.service"
echo "View logs with: sudo journalctl -u turfmanagement.service"
echo ""
