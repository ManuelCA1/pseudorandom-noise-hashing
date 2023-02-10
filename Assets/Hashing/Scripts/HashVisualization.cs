﻿using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

public class HashVisualization : MonoBehaviour
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    struct HashJob : IJobFor
    {
        [ReadOnly]
        public NativeArray<float3x4> positions;

        [WriteOnly]
        public NativeArray<uint4> hashes;

        public SmallXXHash4 hash;

        public float3x4 domainTRS;

        public void Execute(int i)
        {
            float4x3 p = transpose(positions[i]);

            int4 u = (int4)floor(p.c0);
            int4 v = (int4)floor(p.c1);
            int4 w = (int4)floor(p.c2);
            
            hashes[i] = hash.Eat(u).Eat(v).Eat(w);
        }
    }

    [SerializeField] SpaceTRS domain = new SpaceTRS {
        scale = 8f
    };

    static int
        hashesId = Shader.PropertyToID("_Hashes"),
        positionsId = Shader.PropertyToID("_Positions"),
        normalsId = Shader.PropertyToID("_Normals"),
        configId = Shader.PropertyToID("_Config");
    
    [SerializeField]
    Mesh instanceMesh;

    [SerializeField]
    Material material;

    [SerializeField, Range(1, 512)]
    int resolution = 16;

	[SerializeField, Range(-0.5f, 0.5f)]
	float displacement = 0.1f;

    [SerializeField]
    int seed;
    
    NativeArray<uint> hashes;

    NativeArray<float3> positions, normals;

    ComputeBuffer hashesBuffer, positionsBuffer, normalsBuffer;

    MaterialPropertyBlock propertyBlock;

    bool isDirty;

    Bounds bounds;

    void OnEnable() 
    {
        isDirty = true;

        int length = resolution * resolution;
        hashes = new NativeArray<uint>(length, Allocator.Persistent);
        positions = new NativeArray<float3>(length, Allocator.Persistent);
        normals = new NativeArray<float3>(length, Allocator.Persistent);
        hashesBuffer = new ComputeBuffer(length, 4);
        positionsBuffer = new ComputeBuffer(length, 3 * 4);
        normalsBuffer = new ComputeBuffer(length, 3 * 4);

        propertyBlock ??= new MaterialPropertyBlock();
        propertyBlock.SetBuffer(hashesId, hashesBuffer);
        propertyBlock.SetBuffer(positionsId, positionsBuffer);
        propertyBlock.SetBuffer(normalsId, normalsBuffer);
        propertyBlock.SetVector(configId, new Vector4(
            resolution, 1f / resolution, displacement
        ));
    }

    void OnDisable() 
    {
        hashes.Dispose();
        positions.Dispose();
        normals.Dispose();
        hashesBuffer.Release();
        positionsBuffer.Release();
        normalsBuffer.Release();
        hashesBuffer = null;
        positionsBuffer = null;
        normalsBuffer = null;
    }

    void OnValidate()
    {
        if (hashesBuffer != null && enabled)
        {
            OnDisable();
            OnEnable();
        }
    }

    void Update() 
    {
        if(isDirty || transform.hasChanged) {
            isDirty = false;
            transform.hasChanged = false;

            JobHandle handle = Shapes.Job.ScheduleParallel(positions, normals, resolution, transform.localToWorldMatrix, default);

            new HashJob
            {
                positions = positions.Reinterpret<float3x4>(3 * 4),
                hashes = hashes.Reinterpret<uint4>(4),
                hash = SmallXXHash.Seed(seed),
                domainTRS = domain.Matrix
            }.ScheduleParallel(hashes.Length / 4, resolution, handle).Complete();

            hashesBuffer.SetData(hashes);
            positionsBuffer.SetData(positions);
            normalsBuffer.SetData(normals);

            bounds = new Bounds(
				transform.position,
				float3(2f * cmax(abs(transform.lossyScale)) + displacement)
			);
        }

        Graphics.DrawMeshInstancedProcedural
        (
            instanceMesh, 0, material, bounds, hashes.Length, propertyBlock
        );
    }
}
