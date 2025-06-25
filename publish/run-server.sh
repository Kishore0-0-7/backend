#!/bin/bash
cd "$(dirname "$0")"
dotnet turfmanagement.dll --urls=http://0.0.0.0:5125
