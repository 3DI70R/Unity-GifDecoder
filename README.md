# #readme work in progress

Custom gif decoder written from scratch, designed for Unity engine.

There is no gif decoding library for .net, since GifBitmapDecoder is already included in PresentationCore.dll,
but you cant use it in Unity (Since mono doesn't support WPF).

With this library you can decode .gif file from any Stream (file, network, memory, you name it) from any thread.

Features:
- 
- Full format support (87a, 89a, transparency, interlacing, discard methods, etc)
- Can be invoked from any thread (since there is no Unity api involved in decoding)
- Uses as little memory allocations as possible
- Extensively tested on thousands of BTTV emotes
- Uses Color32[] for color manipulation (Faster texture upload speeds)

Portability
-
This library is designed for use in Unity engine, but if for some reason you want to port this somewhere else, 
one and only engine-dependant class is "UnityEngine.Color32", swap it with your implementation and port is completed. 

TODO:
-
- Finish this readme
- NETSCAPE2.0 extension support
- Gif asset importer?
- Utility class for common usecases?
