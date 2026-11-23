# Records the rocket demo's flight as five sensor CSVs, the shape FlightH7Sim's Launch/ folder holds.
#
#   python Demo/Sim/Rocket/generate.py
#
# The vehicle model is Apps/rocket.src's FakeVehicle and the five sensors are its Fake* blocks, run here
# in float32 so the numbers are the ones a C++ float would hold. 3000 rows at 100 Hz is thirty seconds,
# where FlightH7Sim's own Launch/ recording is 6000 rows, sixty seconds at the same rate.

import csv
import math
import os

import numpy as np

f32 = np.float32

# ---- Apps/rocket.src ----
CYCLE_NS = 10000000             # Hz_100, the rate FlightH7 flies at
SECONDS = 30

# Events are written in SECONDS and converted, so the recording says the same thing at any rate.
def at(seconds):
	return int(seconds * 1000000000 / CYCLE_NS)

IGNITION_CYCLE = at(5.5)
BURNOUT_CYCLE = at(6.0)
ARM_CYCLE = at(5.2)

# The operator thinks better of it for a tenth of a second: the machine walks Armed -> Idle -> Armed.
DISARM_CYCLE = at(5.3)
REARM_CYCLE = at(5.4)

# Safed after touchdown, then relaunched, which is the only edge back out of Landed.
SAFE_CYCLE = at(17.8)
RELAUNCH_CYCLE = at(18.0)
RELAUNCH_END = at(18.3)

# Satellites lost while waiting on the pad again, which is the machine's Idle -> GpsWait.
GPS_LOST_CYCLE = at(19.0)
GPS_LOST_END = at(19.5)
GPS_LOST_SATS = 3
THRUST_ACCEL = f32(40.0)
CHUTE_RATE = f32(-15.0)
WIND_SPEED = f32(10.0)
PAD_EAST_PER_DEG = 93375.0
PAD_LAT = 32.9905
PAD_LNG = -106.9750
FIELD_ELEVATION = 1401.0

# ---- Lib/Calcs/Flight/Types.src ----
GRAVITY = f32(9.81)
BARO_SCALE = f32(44330.0)
BARO_EXPONENT = f32(0.1903)

# GpsFix::Fix3D, the fourth member.
FIX_3D = 3

ROWS = at(SECONDS)
DT = f32(f32(CYCLE_NS) / f32(1000000000.0))


def pressure(height):
	return f32(f32(101325.0) * f32(math.pow(f32(f32(1.0) - f32(height / BARO_SCALE)), f32(f32(1.0) / BARO_EXPONENT))))


def flight():
	height = f32(0.0)
	rate = f32(0.0)
	downrange = f32(0.0)

	for tick in range(ROWS):
		accel = f32(0.0)
		if IGNITION_CYCLE <= tick < BURNOUT_CYCLE:
			accel = THRUST_ACCEL
		elif tick >= BURNOUT_CYCLE and height > f32(0.0) and rate > CHUTE_RATE:
			accel = f32(f32(0.0) - GRAVITY)

		rate = f32(rate + f32(accel * DT))
		height = f32(height + f32(rate * DT))

		if height > f32(0.0) and rate < CHUTE_RATE:
			rate = CHUTE_RATE
			accel = f32(0.0)

		if height <= f32(0.0) and tick > IGNITION_CYCLE:
			height = f32(0.0)
			rate = f32(0.0)
			accel = f32(0.0)

		if height > f32(0.0):
			downrange = f32(downrange + f32(WIND_SPEED * DT))

		yield tick, height, rate, accel, downrange


def write(name, header, rows):
	path = os.path.join(os.path.dirname(os.path.abspath(__file__)), name)
	with open(path, "w", newline="\n") as handle:
		writer = csv.writer(handle, lineterminator="\n")
		writer.writerow(header)
		writer.writerows(rows)
	print(f"{name}: {len(rows)} rows")


baro = []
imu = []
mag = []
gps = []
cmd = []

apogee = 0.0
landed = None

for tick, height, rate, accel, downrange in flight():
	apogee = max(apogee, float(height))
	if landed is None and tick > IGNITION_CYCLE and height == f32(0.0):
		landed = tick

	# Barometer: the formula Fake_Pressure runs backwards, at the vehicle's height.
	baro.append([f"{21.5:.2f}", f"{pressure(height):.2f}", 1])

	# IMU: a still gyro with a bias to find, and an accelerometer measuring support rather than motion.
	acc_x = f32(-(f32(accel + GRAVITY) / GRAVITY))
	imu.append([f"{23.0:.2f}", f"{0.05:.6f}", f"{0.0:.6f}", f"{0.0:.6f}",
		f"{acc_x:.6f}", f"{0.0:.6f}", f"{0.0:.6f}", 1])

	# Magnetometer: a field pointing north-and-down, unchanging.
	mag.append([f"{22.0:.2f}", f"{0.2:.4f}", f"{0.4:.4f}", f"{0.9:.4f}", 1])

	# Receiver: latitude fixed, longitude carried east by the wind, altitude the field plus the vehicle.
	airborne = height > f32(0.0)
	seconds = tick * CYCLE_NS // 1000000000
	sats = GPS_LOST_SATS if GPS_LOST_CYCLE <= tick < GPS_LOST_END else 11
	gps.append([
		1, f"{PAD_LAT:.7f}", f"{PAD_LNG + float(downrange) / PAD_EAST_PER_DEG:.7f}", FIX_3D,
		1, 2026, 8, 18,
		1, 12, seconds // 60, seconds % 60, tick % (1000000000 // CYCLE_NS),
		1, f"{10.0 if airborne else 0.0:.4f}",
		1, f"{90.0:.2f}",
		1, f"{FIELD_ELEVATION + float(height):.2f}",
		sats, f"{0.9:.1f}", tick + 1, 0,
		1])

	# The operator's arm switch, thrown once calibration has had time to finish, and dropped to recover.
	armed = tick >= ARM_CYCLE and not (DISARM_CYCLE <= tick < REARM_CYCLE) and tick < SAFE_CYCLE
	relaunching = RELAUNCH_CYCLE <= tick < RELAUNCH_END
	cmd.append([1 if armed else 0, 1 if relaunching else 0])

write("baro.csv", ["temp", "pressure", "updated"], baro)
write("imu.csv", ["temp", "gyro_x", "gyro_y", "gyro_z", "acc_x", "acc_y", "acc_z", "updated"], imu)
write("mag.csv", ["temp", "out_x", "out_y", "out_z", "updated"], mag)
write("gps.csv", [
	"loc_valid", "lat", "lng", "fix",
	"date_valid", "year", "month", "day",
	"time_valid", "hour", "minute", "second", "subseconds",
	"speed_valid", "speed",
	"course_valid", "course",
	"alt_valid", "altitude",
	"sats", "hdop", "stats_valid", "stats_invalid",
	"updated"], gps)
write("cmd.csv", ["arm_cmd", "relaunch_cmd"], cmd)

print(f"apogee {apogee:.1f} m, down at cycle {landed}")
