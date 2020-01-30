#!/bin/bash

fleet_id=$1

dotnet run client --fleet-id ${fleet_id} --search 2>&1 &
pid_1=$!
echo ${pid_1}

sleep 10

dotnet run client --fleet-id ${fleet_id} --search > /dev/null 2>&1 &
pid_2=$!
echo ${pid_2}

dotnet run client --fleet-id ${fleet_id} --search > /dev/null 2>&1 &
pid_3=$!
echo ${pid_3}

dotnet run client --fleet-id ${fleet_id} --search > /dev/null 2>&1 &
pid_4=$!
echo ${pid_4}

trap ctrl_c INT
function ctrl_c() {
  kill ${pid_1}
  kill ${pid_2}
  kill ${pid_3}
  kill ${pid_4}
  exit 0
}

wait ${pid_1} ${pid_2} ${pid_3} ${pid_4}
