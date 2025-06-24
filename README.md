# TurfManagement Application

## Deployment Instructions

### Quick Deployment

1. Transfer both the `turfmanagement-deploy.tar.gz` and `deploy.sh` files to your server
2. Make the deploy script executable:
   ```bash
   chmod +x deploy.sh
   ```
3. Run the deployment script:
   ```bash
   ./deploy.sh
   ```

### Manual Deployment

If you prefer to deploy manually:

1. Extract the application:
   ```bash
   mkdir -p /opt/turfmanagement
   tar -xzf turfmanagement-deploy.tar.gz -C /opt/turfmanagement
   ```

2. Run the application (temporary):
   ```bash
   cd /opt/turfmanagement
   dotnet turfmanagement.dll --urls=http://0.0.0.0:5125
   ```

3. To run as a service, create a systemd service file at `/etc/systemd/system/turfmanagement.service`

### Configuration

The application is configured to run on port 5125.

- Default URL: `http://localhost:5125`
- To allow external access, set the URL to `http://0.0.0.0:5125` in appsettings.json

### Database Connection

The application is configured to connect to:
- Host: database-1.ctec8u86oi32.eu-north-1.rds.amazonaws.com
- Port: 5432
- Database: turrfzone

If you need to change this configuration, modify the `ConnectionStrings` section in `appsettings.json`.

### AWS Lightsail Configuration

If running on AWS Lightsail:

1. Ensure port 5125 is open in your instance's firewall:
   - Go to the Lightsail console
   - Select your instance
   - Go to the "Networking" tab
   - Add a custom rule to allow TCP on port 5125

2. Update the application URL to listen on all interfaces:
   - Edit `appsettings.json` and change the Kestrel URL to `http://0.0.0.0:5125`
