#!/usr/bin/env bash

TARGET_ENV="Debug"
BUILD_VERSION=$(date +%Y%m%d_%H%M%S)

cd $(dirname $0)/gamelift-server-sample-core
dotnet publish -c $TARGET_ENV -r linux-x64

aws gamelift upload-build \
  --name gamelift-server-sample-core \
  --build-version $BUILD_VERSION \
  --build-root bin/${TARGET_ENV}/netcoreapp3.1/linux-x64/publish \
  --operating-system AMAZON_LINUX \
  --region ap-northeast-1

