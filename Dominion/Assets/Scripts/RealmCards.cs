using System.Collections.Generic;
using UnityEngine;

public abstract class RealmCards
{
    public string Name { get; private set; }

    public int PlusActions { get; private set; }
    public int PlusMoney { get; private set; }

    public virtual void Initialise(string name, int plusActions, int plusMoney)
    {
        Name  = name;
        PlusActions = plusActions;
        PlusMoney = plusMoney;
    }
}
