namespace RimTouch
{
    internal enum TouchOwner
    {
        None,
        Ui,
        LocalMap,
        WorldMap,
        LocalMapTool,
        WorldMapTool
    }

    internal enum TouchMode
    {
        None,
        OneFinger,
        MapPan,
        SelectionHold,
        WorldDragBox,
        TwoFinger,
        VanillaMapTool
    }
}
