#!/usr/bin/env bash

TARGET_ENV="Release"
BUILD_VERSION=$(git rev-parse HEAD)

cd $(dirname $0)/gamelift-server-sample-core
dotnet publish -c $TARGET_ENV -r linux-x64

aws gamelift upload-build \
  --name gamelift-server-sample-core \
  --build-version $BUILD_VERSION \
  --build-root bin/${TARGET_ENV}/netcoreapp3.1/linux-x64/publish \
  --operating-system AMAZON_LINUX \
  --region ap-northeast-1

