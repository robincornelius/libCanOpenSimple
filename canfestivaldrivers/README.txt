These drivers are based on the CanFestival driver model. The original code was inspired by canfestival and some it can be directly traced back hence the canfestival copyright and licence on this part.

CanFestival also features a number of other drivers for other can boards and linux all of these *could* be useful but all of them will require modifiying

I had added extra API functions to the drivers so that they can self enumerate, that is each driver should enumerate the number of connected devices that it cares about and report this to the calling application.

