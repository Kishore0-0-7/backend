#!/bin/bash

# Change to the application directory
cd "$(dirname "$0")"

# Export ASP.NET Core production settings
export ASPNETCORE_ENVIRONMENT=Production

# Start the application with 0.0.0.0 binding to accept connections from any IP
dotnet turfmanagement.dll --urls=http://0.0.0.0:5125
