#!/usr/bin/env bash

TARGET_ENV="Release"
TARGET_PLATFORM="linux-x64"
PUBLISH_DIR="bin/${TARGET_ENV}/netcoreapp3.1/${TARGET_PLATFORM}/publish"
BUILD_VERSION=$(git rev-parse HEAD)

cd $(dirname $0)/gamelift-server-sample-core
rm -rf $PUBLISH_DIR
dotnet publish -c $TARGET_ENV -r $TARGET_PLATFORM

aws gamelift upload-build \
  --name gamelift-server-sample-core \
  --build-version $BUILD_VERSION \
  --build-root $PUBLISH_DIR \
  --operating-system AMAZON_LINUX_2 \
  --region ap-northeast-1

