#!/bin/bash
cd "$(dirname "$0")"
dotnet turfmanagement.dll --urls=http://localhost:5125
