using UnityEngine;
using System.Collections.Generic;

[SelectionBase]
public class TerrainChunk : MonoBehaviour {
	const int CHUNK_VOXELS_DIM = Terrain.CHUNK_VOXELS_DIM;
	const float CHUNK_SIZE = Terrain.CHUNK_SIZE;
	const float VOXEL_SIZE = Terrain.VOXEL_SIZE;

	public const int LAYERID_SOLID = 9;
	public const int LAYERMASK_SOLID = 1 << LAYERID_SOLID;

	private Voxel[] voxels;

	private Mesh mesh;
	private Transform meshes; // grouping for submeshes

	private List<Vector3> gen_vertices;
	private List<int>     gen_triangles;

	private List<Vector2> gen_uv;
	private Vector2[]     vox_uv;

	int chunkX;
	int chunkY;

	// Takes voxels chunk from big map
	public void Initialize() {
		//this.chunk_size = chunk_sz;
		//gridSize = size;
		//voxelSize = size / (float)chunk_sz;

		mesh = null;
		meshes = transform.Find("Meshes");

		// TODO: optimize this with preallocated array
		gen_vertices  = new List<Vector3>();
		gen_uv        = new List<Vector2>();
		vox_uv        = new Vector2[4];
		gen_triangles = new List<int>();
	}

	// Given new chunk of voxels, reset and rebuild mesh
	public void UseVoxels(Voxel[] src_voxels, int chunkx, int chunky) {
		// If this chunk already displays given voxels, do nothing
		if (this.voxels == src_voxels) {
			return;
		}

		this.chunkX = chunkx;
		this.chunkY = chunky;
		this.voxels = src_voxels;
		RebuildMesh();	
	}

	public bool IsUsingVoxels(Voxel[] v) {
		return this.voxels == v;
	}

	private void RebuildMesh () {
		gen_vertices.Clear();
		gen_uv.Clear();
		gen_triangles.Clear();

		if (mesh != null) { DestroyImmediate(mesh); }
		mesh = new Mesh();
		mesh.name = "Generated Terrain Mesh";

		TriangulateCellRows();
		RebuildColliders();

		mesh.vertices  = gen_vertices.ToArray();
		mesh.triangles = gen_triangles.ToArray();
		mesh.uv        = gen_uv.ToArray();
		mesh.RecalculateNormals();
		GetComponent<MeshFilter>().mesh = mesh;

		gen_vertices.Clear();
		gen_uv.Clear();
		gen_triangles.Clear();
	}

	void RebuildColliders ()
	{
		while (meshes.transform.childCount > 0) {
			DestroyImmediate(meshes.transform.GetChild(0).gameObject);
		}

		Vector2 chunkOrigin = new Vector2(chunkX * CHUNK_SIZE, chunkY * CHUNK_SIZE); //voxels[0].position;

		// Scan horizontally all chunk voxels, and group them into solid rows
		// Create one child GameObject for each row
		for (int y = 0; y < CHUNK_VOXELS_DIM; y++) {
			GameObject coll_go = new GameObject();
			coll_go.layer = LAYERID_SOLID;
			coll_go.transform.parent = meshes.transform;

			BoxCollider2D   bc = coll_go.AddComponent<BoxCollider2D>();
			bc.transform.parent = coll_go.transform;
			bc.name = "Box Collider R" + y;
			Vector2    bcBegin = chunkOrigin + new Vector2(0, y * VOXEL_SIZE);
			int        bcWidth = 0;

			for (int x = 0; x < CHUNK_VOXELS_DIM; x++) {
				if (voxels[y * CHUNK_VOXELS_DIM + x].IsSolid()) { 
					bcWidth++;
					continue; 
				}
				if (bcWidth > 0) {
					AddBoxColliderTo(coll_go, ref bc, bcBegin, bcWidth);
					// Make new collider to continue
					bc = coll_go.AddComponent<BoxCollider2D>();
					bc.transform.parent = coll_go.transform;
					bc.name = "Box Collider R" + y;
					bcBegin.x += (bcWidth + 1) * VOXEL_SIZE;
					bcWidth = 0;
				} else {
					bcBegin.x += VOXEL_SIZE;
				}
			}
			// Finalize if there was open counting row
			if (bcWidth > 0) {
				AddBoxColliderTo(coll_go, ref bc, bcBegin, bcWidth);
			}
			if (bc != null) { Destroy (bc); }
		}
	}

	// Takes empty boxcollider and sets it to cover row of voxels starting at bcBegin
	// which has length bcWidth, bc is then attached to collision gameobjects
	void AddBoxColliderTo (GameObject coll_go, ref BoxCollider2D bc, Vector2 bcBegin, int bcWidth)
	{
		bc.offset = bcBegin + new Vector2(bcWidth * VOXEL_SIZE * 0.5f, VOXEL_SIZE * 0.5f);
		bc.size   = new Vector2(bcWidth * VOXEL_SIZE, VOXEL_SIZE);
		//Debug.Log (bc.offset.ToString() + " sz=" + bc.size.ToString());
		bc = null; // forget it, leave hanging
	}

	private void TriangulateCellRows () {
		for (int i = 0, y = 0; y < CHUNK_VOXELS_DIM; y++) {
			for (int x = 0; x < CHUNK_VOXELS_DIM; x++, i++) {
				TriangulateCell(voxels[i]);
			}
		}
	}

	private void TriangulateCell (Voxel a) {

		if (a.vtype != VoxelType.Empty) {
			AddQuad(a);

			a.GetUV(vox_uv);
			gen_uv.Add(vox_uv[0]);
			gen_uv.Add(vox_uv[1]);
			gen_uv.Add(vox_uv[2]);
			gen_uv.Add(vox_uv[3]);
		}
	}

	private void AddQuad (Voxel vx) {
		Vector2 a = vx.position;
		Vector2 b = new Vector2(vx.opposite.x, a.y);
		Vector2 c = new Vector2(a.x, vx.opposite.y);
		Vector2 d = vx.opposite;
		//Debug.Log ("a=" + a.ToString() + " d=" + d.ToString());

		int vertexIndex = gen_vertices.Count;
		gen_vertices.Add(a);
		gen_vertices.Add(b);
		gen_vertices.Add(c);
		gen_vertices.Add(d);

		gen_triangles.Add(vertexIndex);
		gen_triangles.Add(vertexIndex + 2);
		gen_triangles.Add(vertexIndex + 3);
		gen_triangles.Add(vertexIndex);
		gen_triangles.Add(vertexIndex + 3);
		gen_triangles.Add(vertexIndex + 1);
	}

	public void Edit (int x, int y) {
		var newVT = VoxelType.Empty;
		int voxIndex = x + y * CHUNK_VOXELS_DIM;
		if (voxIndex < 0 || voxIndex >= CHUNK_VOXELS_DIM * CHUNK_VOXELS_DIM) {
			return; 
		}
		if (voxels[voxIndex].vtype != newVT) {
			voxels[voxIndex].vtype = newVT;
			RebuildMesh();
		}
	}
}