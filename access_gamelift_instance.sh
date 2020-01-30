#!/bin/bash

fleet_id=$1

instance_id=$(aws gamelift describe-instances --fleet-id $fleet_id |jq -r '.Instances[] |if .Status == "Active" then .InstanceId else empty end' |head -1)
aws gamelift get-instance-access --fleet-id $fleet_id --instance-id $instance_id |tee gamelift_creds.json
jq -r .InstanceAccess.Credentials.Secret gamelift_creds.json |tee gamelift.pem
chmod 600 gamelift.pem

ssh -i gamelift.pem gl-user-remote@$(jq -r .InstanceAccess.IpAddress gamelift_creds.json)
