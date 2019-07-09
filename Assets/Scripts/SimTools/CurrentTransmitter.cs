using UnityEngine;


public class CurrentTransmitter : MonoBehaviour         //Transmits current
{
    public GameObject Input1;
    public GameObject Output1;

    public bool isConnected;                            //Tells if Input and Output are connected                    
    public bool setOnListOnce;

    public ConnectorLine cLine;                         //Line that connects things

    void Start()
    {
        if (cLine.StartPoint.CompareTag("Output"))
        {
            Output1 = cLine.StartPoint;
        }

        setOnListOnce = true;
        
    }

    void Update()
    {
        if (!MainManager.IsSimMode)                    //Do things in paused mode                 
        {
            Connecting();
            if (isConnected && setOnListOnce)
            { 
                Input1.gameObject.GetComponent<InputC>().outputList.Add(Output1); // Adds output to input list  of outputs if theyre connected
                setOnListOnce = false;
            }
        }
        //if (MainManager.IsSimMode)                     //Do things in sim mode
        //{
            
        //}
    }
    public void Connecting()                         //Trying connect input and output, and if both exist, sets isConnected bool to true;                  
    {
        if (Input1 != null && Output1 != null)
        {
            isConnected = true;
        }
        else
        {
            isConnected = false;
        }
    }
}
