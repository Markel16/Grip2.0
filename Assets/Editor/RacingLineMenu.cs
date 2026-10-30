using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RacingLineMenu
{
    [MenuItem("Grip/Generar trazada de la IA")]
    private static void GenerateRacingLine()
    {
        ArcadeCarController playerCar = FindPlayerCar();
        if (playerCar == null)
        {
            EditorUtility.DisplayDialog("Trazada de la IA", "No se encontró el coche del jugador (un ArcadeCarController sin ArcadeWaypointAI).", "OK");
            return;
        }

        RacingLine racingLine = Object.FindAnyObjectByType<RacingLine>();
        if (racingLine == null)
        {
            GameObject lineObject = new GameObject("RacingLine");
            Undo.RegisterCreatedObjectUndo(lineObject, "Crear trazada de la IA");
            racingLine = lineObject.AddComponent<RacingLine>();
        }

        // El sentido de carrera se toma del coche; si está de lado respecto a la pista, de la línea de meta.
        List<Vector3> directionHints = new List<Vector3> { playerCar.WorldForward };
        LapFinishTrigger finishLine = Object.FindAnyObjectByType<LapFinishTrigger>();
        if (finishLine != null)
        {
            directionHints.Add(finishLine.transform.forward);
        }

        Undo.RecordObject(racingLine, "Generar trazada de la IA");
        if (!racingLine.Generate(playerCar.transform.position, directionHints.ToArray(), out string error))
        {
            EditorUtility.DisplayDialog("Trazada de la IA", error, "OK");
            return;
        }

        FinishEdit(racingLine);
        Debug.Log($"Trazada de la IA generada: {racingLine.Count} puntos (~{racingLine.Count * racingLine.PointSpacing:F0} m de vuelta). "
            + "Si la IA debe ir en sentido contrario usa Grip > Invertir sentido de la trazada.", racingLine);
    }

    [MenuItem("Grip/Invertir sentido de la trazada")]
    private static void ReverseRacingLine()
    {
        RacingLine racingLine = Object.FindAnyObjectByType<RacingLine>();
        if (racingLine == null || racingLine.Count < 3)
        {
            EditorUtility.DisplayDialog("Trazada de la IA", "Primero genera la trazada con Grip > Generar trazada de la IA.", "OK");
            return;
        }

        Undo.RecordObject(racingLine, "Invertir sentido de la trazada");
        racingLine.Reverse();
        FinishEdit(racingLine);
    }

    private static void FinishEdit(RacingLine racingLine)
    {
        EditorUtility.SetDirty(racingLine);
        EditorSceneManager.MarkSceneDirty(racingLine.gameObject.scene);
        Selection.activeObject = racingLine.gameObject;
        SceneView.RepaintAll();
    }

    private static ArcadeCarController FindPlayerCar()
    {
        foreach (ArcadeCarController car in Object.FindObjectsByType<ArcadeCarController>())
        {
            if (car.GetComponentInParent<ArcadeWaypointAI>() == null)
            {
                return car;
            }
        }

        return null;
    }
}
