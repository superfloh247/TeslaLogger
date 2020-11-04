MVP

preparation: record a session with teslalogger. it makes sense to start the recording when the car is asleep

idea behind that: TL's first request is always /api/1/vehicles, so all JSON files before the first vehicles.json are ignored by the mockserver import

if you run mockserver.exe in the same directory as teslalogger.exe, then you're all set. otherwise copy the entire JSON directory the mockserver.exe's directory.

maybe it's a good idea to clone your teslalogger DB into a new one

edit database credentials in mockserver/Database.cs and recompile


run mockserver: mono mockserver.exe

expected:

HttpListener bound to http://*:http://*:24780// (APIServer.cs:78)


open http://hostname:24780/mockserver/listJSONDumps in your browser, this should list all recorded TL sessions

import just one (that's enough to start with)

now change teslalogger to connect to mockserver instead if tesla

edit WebHelper.cs

public static readonly string apiaddress = "http://hostname:24780/";

recompile TL

start TL

you should now see in the console where mockserver runs a lot of incoming requests and JSON responses

