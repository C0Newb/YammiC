# Yammi-C

An app to connect your phone to Yamaha CCU in newer bikes. Written in C# with [Avalonia](https://avaloniaui.net/) (Android, with desktop for development).

## Feature Support

| Feature | Description | Implemented | Confirmed working |
| -- | -- | -- | -- |
| Media control | Music metadata, playback control, volume control (steps the media stream silently, so it also moves a Bluetooth headset) | ✅ | ✅ - occasional bug with "hanging" metadata |
| Phone control | Answer, decline, hangup, caller id | ⚠️ | ⚠️ - caller id is broken. |
| Weather| Weather forecasts sent to the bike | ✅ | ⚠️ - works, but mock data. |
| Notifications | Send device notifications to the bike | ⚠️ | ✅ - can send mock notifications |
| Virtual Pit Board | Send images to the VPB on the "track" theme | ✅ | ✅ |
| Screen builder | Design your own navigation and pit board screens (text, clock, music, bike data) and import/export them as JSON | ✅ | ⚠️ - editor works, on-bike result not fully checked |
| Navigation screen | Own link to the bike's navigation display: the built-in layout, your custom screen, or Reels (videos) | ⚠️ | ⚠️ - partly checked |
| Bike data (CAN) | Live RPM, water and intake temperature, gear, throttle (TPS/APS), bank angle, pitch rate, acceleration; usable in the screen builder | ✅ | ⚠️ - throttle and gear confirmed; other decodings unverified |
| Fuel level | Tank level from the bike | ❌ | ❌ - no CAN signal for it found; an explorer on the Bike data page can request other IDs |
| Ride modes (Y-Trac) | Read the Custom and Track profiles from the bike, keep them in a library on the phone, export/import as JSON, send back | ⚠️ | ❌ - untested on a bike |
| Lap timer | Start/lap/stop (debug page) that sends laps to the bike | ⚠️ | ❌ - untested on a bike |

## Motivation

The official "Y-Connect" app is okay, and for the most part works. My biggest issue with it is:

1) Requires a constant internet connection
2) Requires constant location data
3) According to the location permissions page on my phone, _may_ share location data with third-parties (the fuck?!?)

Now - personally I don't want my location data and how I drive/use **my** _sports_ bike to be sold to an insurance company or reporting agency.
I don't know if Yamaha does this, I'm not suggesting they do, but I'd sleep better at night knowing, with no-uncertainty, that the app doesn't.

The only way I know it wouldn't is if I can disable the internet connection for the app, but it **refuses** to run without an internet connection (how weird, huh?).

So, the next best thing is to just make my own. It's **my** bike and **my** phone after all.

So that's what I did with the help of Claude.

## Note on AI Usage

I wasn't going to spend the months required to figure out and understand the way the official app works by staring, poking and proding at my device's Bluetooth activity...

So yes, I used Claude massively to understand how the hell the official app communicates with my bike.

Claude was used in writing some of the Android code, especially the UI because I do not like building those.
With that said, I made sure to go through a lot of the YMC SDK (the code communicating with the bike) to ensure it's good quality.

I re-wrote code when necessary (Claude can write some ugly ass code). I've instructed Claude to put a "Written using an LLM." at the top of all files it creates.

If you have issue with this: I don't care. I wanted something that worked without spending a bunch of time, that could otherwise be spent _riding_ my bike, writing this app.

## Others

I want to call out the [Pillion](https://github.com/alexandrevega/pillion) project! 
I used that repo for the research and development of Nav related functions of _this_ app! This repo is **not** intended to replace the work Pillion and team have done.
We're two different apps with two different goals. I will not implement any kind of screen-sharing/casting into YammiC as:

1) They already have that covered,
2) I'm lazy,
3) Claude cannot be trusted to not just steal their code.

And obviously Yamaha has an app that does more than we do. **Under no circumstances should any code from that app be used within this project.**
Code can be copyrighted, and is, but _what_ it does .. not so much.
The data packets sent to the bike are **not** copyrightable, those packets, via a Bluetooth snoop, are to be used for research purposes.
