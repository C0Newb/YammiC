# TO-DO

## SDK

- [ ] Weather
  - [x] Display
  - [ ] Pull details from (an) API
- [x] Media
  - [x] Track details
  - [x] Media controls (volume, next/prev, play/pause)
  - [ ] Fix volume control to control Cardo
  - [ ] Fix rare "hang" bug where the next track info isn't displayed
- [ ] Phone
  - [x] Incoming calls (numbers/contact name)
  - [x] Answer/hang up, volume
  - [x] Missed calls
- [ ] Notifications
  - [ ] Update
  - [ ] Delete
  - [x] Create
- [ ] CCU pairing
  - [ ] Get passkey via Yamaha API (with login credentials)
  - [ ] Instruct bike to pair
- [x] Virtual Pit Board
  - [x] Send basic text message
- [ ] Navigation
  - [x] Send images
  - [x] Zoom-in/zoom-out button listeners
  - [ ] "Go home" button listener
  - [ ] "Go to work" button listener
  - [ ] "Near by gas stations" button listener
  - [x] Navigation specific 'sdk' class (listener events subscriptions, activate()/deactive() navigation integration, sendImage())
- [ ] Y-Tracc information transfer (built, untested on a bike; see docs/ride-modes.md)
  - [x] Receive settings
  - [x] Send settings
  - [x] Track/lap timer (debug page only)
- [ ] Bike telementry
  - [x] RPM
  - [x] Speed
  - [ ] Fuel usage
  - [ ] SPIKE: discover what else may be available

### Stretch
- [ ] CAN bus messages

## APP

- [ ] Redo UI
- [x] Better handling of waiting/listening for the bike (sit in background and wait, don't crash, be gentle on device resources)
- [ ] Settings page
- [ ] Developer/debug page with various buttons and toggables
- [ ] Pairing (in-conjugation with CCU pairing on SDK)
  - [ ] Prompt for Yamaha login details
  - [ ] Pass details to SDK to get passkey
  - [ ] Store pair information - allow multiple bike's data
- [ ] Weather (hooked up to NWS)
- [x] Music provider
- [ ] Notifications
- [ ] Turn-by-turn with Google maps (not the "navigation map" per-say)
- [x] Send music data via Virtual Pit Board (VPB)
- [x] VPB
  - [ ] Send fuel info
  - [ ] Turn-by-turn
- [ ] Implement 'control' hub via navigation button listeners (go home/go to work/near by gas stations)
- [ ] Android Auto streamed as "navigation" - integration with buttons
