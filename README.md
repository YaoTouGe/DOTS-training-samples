## DOTS Performance Compared with Original

When porting each sample I met some interesting problems to solve, so I wrote it in this README, the original sample docs is [here](DOTS_example_doc.md). Below is the performance comparision I get:

|||CPU Time(ms) in Editor|||
|:-:|:-:|:-:|:-:|:-:|
|Sample| Original|DOTS no burst| DOTS single thread|DOTS multi-thread|
|AntPheromone|1.74|*5.7*|0.36|0.14|

### AntPheromone

The first version is even two times slower than Monobehavior, after deep profiling, much time is spent on `NativeArray.GetItem`, it performs some safty check when accessed from C#.

What's more, it can't be burst compiled since burst doesn't support non-readonly static members (I can't put it in components member field). At last I use DynamicBuffer component and set the `InternalBufferCapacity` to zero to make it out of chunck memory. final it gets about 6 times faster than original.

Another interesting thing is if I disable burst compile for the AntMoveSystem, it becomes slower again. So if code is written the DOTS way(use NativeArray or DynamicBuffer and so on), but without burst compile enabled, the code can be even less performant than MonoBehavior way in editor. I guess it's because without burst compile, there could bring in overhead like safty and atomic check like the first situation. Maybe I should test it on built player with il2cpp later. 

The multi-thread DOTS result is quite unfair since it off-load workds to other threads without waiting. So I call complete to sync explicitly to get the real excution time. It's also some tricky to make random generator work for each job exection, I use the way in this [link](https://ennogames.com/blog/random-numbers-inside-unity-jobs).
