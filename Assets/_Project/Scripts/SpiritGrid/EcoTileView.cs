// Tile views react to six axial neighbours and retain their construction state.
public interface IEcoTileView
{
    void SetNeighbors(HexDirectionMask mask);
    void SetBuildState(BuildState state);
}
