#!/usr/bin/env bash

aws gamelift create-fleet \
--name gamelift-server-sample-core \
  --build-id $1 \
  --ec2-instance-type c5.large \
  --ec2-inbound-permissions file://inbound-permission-config.json \
  --fleet-type ON_DEMAND \
  --new-game-session-protection-policy FullProtection \
  --runtime-configuration file://runtime-config.json \
  --metric-groups gamelift-server-sample-core

