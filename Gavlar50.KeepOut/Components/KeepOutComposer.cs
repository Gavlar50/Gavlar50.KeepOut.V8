using Umbraco.Core;
using Umbraco.Core.Composing;

namespace Gavlar50.KeepOut.Components
{
    public class KeepOutComposer : IUserComposer
    {
        public void Compose(Composition composition)
        {
            composition.Components().Append<KeepOutComponent>();
        }
    }
}
